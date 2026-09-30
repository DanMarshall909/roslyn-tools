using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services.Results;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public sealed class FindUnusedWorkspaceHandlerTests
{
    [Fact]
    public async Task incomplete_mcp_envelope_retains_all_projects_and_cli_can_request_full_json()
    {
        using var builder = new AdhocProjectBuilder("Domain");
        builder.AddSource("DomainUnion.cs", string.Join(Environment.NewLine,
            Enumerable.Range(0, 25).Select(index => $"public union DomainResult{index}(Success, Failure);")));
        builder.AddProject("Application").AddSource("ApplicationUnion.cs", string.Join(Environment.NewLine,
            Enumerable.Range(0, 25).Select(index => $"public union ApplicationResult{index}(Accepted, Rejected);")));
        builder.AddProject("Valid").AddSource("Neighbor.cs", "public static class Neighbor { public static void UnusedNeighbor() { } }");
        var (workspace, solution) = builder.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        {
            var result = await provider.GetRequiredService<ToolRegistry>().InvokeAsync("find_unused",
                JsonSerializer.SerializeToElement(new { scope = "methods", solution = "ignored.slnx" }));

            result.isError.ShouldBeFalse();
            result.structuredContent.ShouldNotBeNull();
            using var document = JsonDocument.Parse(result.content);
            var root = document.RootElement;
            root.GetProperty("workspace").GetProperty("state").GetString().ShouldNotBe("Complete");
            var diagnostics = root.GetProperty("diagnostics").EnumerateArray().ToArray();
            diagnostics.Length.ShouldBeGreaterThan(20);
            var compilerDiagnostics = diagnostics.Where(diagnostic =>
                diagnostic.GetProperty("context").GetProperty("origin").GetString() == "compiler").ToArray();
            compilerDiagnostics.Select(diagnostic => diagnostic.GetProperty("context").GetProperty("projectPath").GetString())
                .Distinct().Count().ShouldBeGreaterThanOrEqualTo(2);
            root.GetProperty("data").GetProperty("symbols").EnumerateArray()
                .Select(symbol => symbol.GetProperty("name").GetString()).ShouldContain("UnusedNeighbor");
            foreach (var symbol in root.GetProperty("data").GetProperty("symbols").EnumerateArray())
            {
                symbol.GetProperty("confidence").GetString().ShouldBe("low");
                symbol.GetProperty("suggestedFixes").GetArrayLength().ShouldBe(0);
                symbol.GetProperty("name").GetString()!.ShouldNotContain("Result");
            }

            // The shared response also has to survive daemon-style typed deserialization.
            var typed = JsonSerializer.Deserialize<RtResultEnvelope<FindUnusedAnalysisResult>>(
                result.content, JsonEncoders.Select(false)).ShouldNotBeNull();
            var cliOutput = new FindUnusedEnvelopeResult(typed);
            var previousFullEvidence = RtContext.FullWorkspaceEvidence;
            try
            {
                RtContext.FullWorkspaceEvidence = false;
                using var compactWriter = new StringWriter();
                cliOutput.RenderJson(compactWriter);
                using var compactDocument = JsonDocument.Parse(compactWriter.ToString());
                compactDocument.RootElement.GetProperty("schemaVersion").GetString().ShouldBe("rt.result.v2");
                compactDocument.RootElement.GetProperty("workspace").GetProperty("evidenceSummary")
                    .GetProperty("totalDiagnostics").GetInt32().ShouldBe(diagnostics.Length);

                RtContext.FullWorkspaceEvidence = true;
                using var fullWriter = new StringWriter();
                cliOutput.RenderJson(fullWriter);
                using var fullDocument = JsonDocument.Parse(fullWriter.ToString());
                JsonElement.DeepEquals(root, fullDocument.RootElement).ShouldBeTrue();
            }
            finally
            {
                RtContext.FullWorkspaceEvidence = previousFullEvidence;
            }
        }
    }
}
