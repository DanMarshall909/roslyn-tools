using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class MoveSliceHandlerTests
{
    [Fact]
    public void Descriptor_is_a_typed_review_default_mutation_hidden_from_untrusted_hosts()
    {
        using var untrustedProvider = McpTestHost.CreateProvider();
        using var trustedProvider = McpTestHost.CreateProvider(
            new McpHostOptions(Directory.GetCurrentDirectory()));
        var descriptor = trustedProvider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == MoveSliceCommandDefinition.Definition.Name);
        var tool = trustedProvider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(candidate => candidate.name == MoveSliceCommandDefinition.Definition.Name);

        descriptor.Port.ShouldNotBeNull();
        descriptor.Port.RequestType.ShouldBe(typeof(MoveSliceRequest));
        descriptor.Port.ResponseType.ShouldBe(typeof(MutationExecutionResult<MoveSliceResult>));
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.DryRunByDefault);
        descriptor.Effects.ShouldBe(CommandEffects.RepositoryRead | CommandEffects.RepositoryWrite);
        tool.annotations.readOnlyHint.ShouldBeFalse();
        tool.annotations.destructiveHint.ShouldBeTrue();
        var required = tool.inputSchema["required"].ShouldBeOfType<string[]>();
        required.ShouldBe(["to-project", "to-namespace"], ignoreOrder: true);
        var properties = tool.inputSchema["properties"]
            .ShouldBeOfType<Dictionary<string, Dictionary<string, object>>>();
        properties.Keys.ShouldContain("types");
        properties.Keys.ShouldContain("namespace");
        properties.Keys.ShouldContain("dry-run");
        properties.Keys.ShouldContain("add-internals-visible-to");
        untrustedProvider.GetRequiredService<ToolRegistry>().GetTools()
            .ShouldNotContain(candidate => candidate.name == MoveSliceCommandDefinition.Definition.Name);
    }

    [Fact]
    public async Task Trusted_host_dry_run_invokes_the_shared_port_and_leaves_files_untouched()
    {
        const string sourceText = "namespace Legacy; public sealed class Service { }";
        using var builder = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", sourceText);
        builder.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = builder.Build();
        WriteProjectFiles(solution);
        var sourcePath = Path.Combine(builder.Directory, "Source", "Service.cs");
        var targetPath = Path.Combine(builder.Directory, "Target", "Feature", "Service.cs");

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider(new McpHostOptions(builder.Directory)))
        using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["types"] = new List<string> { "Legacy.Service" },
            ["to-project"] = "Target",
            ["to-namespace"] = "App.Target.Feature"
        })))
        {
            var result = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync(MoveSliceCommandDefinition.Definition.Name, arguments.RootElement);

            result.isError.ShouldBeFalse(result.content);
            result.structuredContent.ShouldNotBeNull();
            var payload = result.structuredContent!.Value;
            payload.GetProperty("status").GetString().ShouldBe(nameof(MutationExecutionStatus.DryRun));
            payload.GetProperty("value").GetProperty("applicable").GetBoolean().ShouldBeTrue();
            File.ReadAllText(sourcePath).ShouldBe(sourceText);
            File.Exists(targetPath).ShouldBeFalse();
        }
    }

    private static void WriteProjectFiles(Solution solution)
    {
        foreach (var project in solution.Projects)
            File.WriteAllText(project.FilePath!, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    }
}
