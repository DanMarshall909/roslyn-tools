using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class FindUnusedWorkspaceOutput
{
    [Theory]
    [InlineData("")]
    [InlineData("--markdown")]
    [InlineData("--llm")]
    [InlineData("--json")]
    public async Task real_cli_preserves_formats_and_marks_incomplete_findings_advisory(string format)
    {
        using var builder = new AdhocProjectBuilder("Domain");
        builder.AddSource("Unsupported.cs", "public union ClaimResult(Success, Failure);");
        builder.AddProject("Application").AddSource("Unsupported.cs", "public union ReviewResult(Accepted, Rejected);");
        builder.AddProject("Valid").AddSource("Valid.cs", "public static class Neighbor { public static void UnusedNeighbor() { } }");
        var (workspace, solution) = builder.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var args = new List<string> { "inspect", "find-unused", "--scope", "methods", "--solution", "ignored.slnx" };
            if (format.Length > 0)
                args.Add(format);
            var (exitCode, output) = await InvokeAsync(args.ToArray());

            exitCode.ShouldBe(0);
            output.ShouldContain("UnusedNeighbor");
            if (format == "--json")
            {
                using var document = JsonDocument.Parse(output);
                var root = document.RootElement;
                root.GetProperty("schemaVersion").GetString().ShouldBe("rt.result.v1");
                root.GetProperty("workspace").GetProperty("state").GetString().ShouldNotBe("Complete");
                var symbols = root.GetProperty("data").GetProperty("symbols").EnumerateArray().ToArray();
                symbols.ShouldNotBeEmpty();
                foreach (var symbol in symbols)
                {
                    symbol.GetProperty("confidence").GetString().ShouldBe("low");
                    symbol.GetProperty("suggestedFixes").GetArrayLength().ShouldBe(0);
                }
                symbols.ShouldNotContain(symbol => symbol.GetProperty("name").GetString() == "ClaimResult");
                symbols.ShouldNotContain(symbol => symbol.GetProperty("name").GetString() == "ReviewResult");
                root.GetProperty("diagnostics").GetArrayLength().ShouldBeGreaterThan(0);
            }
            else
            {
                output.TrimStart().ShouldNotStartWith("{");
                output.ToLowerInvariant().ShouldContain("workspace");
                output.ToLowerInvariant().ShouldContain("low");
                output.ShouldContain("advisory");
                if (format == "--markdown")
                    output.ShouldContain("| kind | name |");
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("--markdown")]
    [InlineData("--llm")]
    public async Task zero_findings_still_show_incomplete_workspace_and_limitations(string format)
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Unsupported.cs", "public union ClaimResult(Success, Failure);");
        var (workspace, solution) = builder.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var args = new List<string> { "find-unused", "--scope", "methods", "--solution", "ignored.slnx" };
            if (format.Length > 0)
                args.Add(format);
            var (exitCode, output) = await InvokeAsync(args.ToArray());

            exitCode.ShouldBe(0);
            output.ToLowerInvariant().ShouldContain("workspace");
            output.ShouldContain("advisory");
        }
    }

    private static async Task<(int ExitCode, string Output)> InvokeAsync(string[] arguments)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var previousFormat = RtContext.Format;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            return (await App.RunAsync(arguments), output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            RtContext.Format = previousFormat;
        }
    }
}
