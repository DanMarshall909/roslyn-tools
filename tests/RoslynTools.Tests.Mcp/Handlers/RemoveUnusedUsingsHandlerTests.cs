using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class RemoveUnusedUsingsHandlerTests
{
    [Fact]
    public void descriptor_is_a_typed_review_default_mutation_hidden_from_untrusted_hosts()
    {
        using var untrustedProvider = McpTestHost.CreateProvider();
        using var trustedProvider = McpTestHost.CreateProvider(
            new McpHostOptions(Directory.GetCurrentDirectory()));
        var descriptor = trustedProvider.GetRequiredService<ApplicationCommandCatalog>()
            .All.Single(candidate =>
                candidate.ToolName == RemoveUnusedUsingsCommandDefinition.Definition.Name);
        var tool = trustedProvider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(candidate =>
                candidate.name == RemoveUnusedUsingsCommandDefinition.Definition.Name);

        descriptor.Port.ShouldNotBeNull();
        descriptor.Port.RequestType.ShouldBe(typeof(RemoveUnusedUsingsRequest));
        descriptor.Port.ResponseType.ShouldBe(
            typeof(MutationExecutionResult<RemoveUnusedUsingsResult>));
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.DryRunByDefault);
        descriptor.Effects.ShouldBe(CommandEffects.RepositoryRead | CommandEffects.RepositoryWrite);
        tool.annotations.readOnlyHint.ShouldBeFalse();
        tool.annotations.destructiveHint.ShouldBeTrue();
        var properties = tool.inputSchema["properties"]
            .ShouldBeOfType<Dictionary<string, Dictionary<string, object>>>();
        properties.Keys.ShouldContain("file");
        properties.Keys.ShouldContain("solution");
        properties.Keys.ShouldContain("dry-run");
        untrustedProvider.GetRequiredService<ToolRegistry>().GetTools()
            .ShouldNotContain(candidate =>
                candidate.name == RemoveUnusedUsingsCommandDefinition.Definition.Name);
    }

    [Fact]
    public async Task trusted_host_dry_run_invokes_the_shared_port_without_writing()
    {
        const string sourceText = "using System;\ninternal sealed class Service { }";
        using var builder = new AdhocProjectBuilder("ImportsApp")
            .AddSource("Service.cs", sourceText);
        var (workspace, solution) = builder.Build();
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(builder.Directory));
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            solution = builder.SolutionPath
        }));

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var result = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync(RemoveUnusedUsingsCommandDefinition.Definition.Name, arguments.RootElement);

            result.isError.ShouldBeFalse(result.content);
            var payload = result.structuredContent!.Value;
            payload.GetProperty("status").GetString()
                .ShouldBe(nameof(MutationExecutionStatus.DryRun));
            payload.GetProperty("value").GetProperty("removedCount").GetInt32().ShouldBe(1);
            File.ReadAllText(Path.Combine(builder.ProjectDir, "Service.cs")).ShouldBe(sourceText);
        }
    }

    [Fact]
    public async Task trusted_host_returns_a_typed_error_for_a_missing_file()
    {
        using var builder = new AdhocProjectBuilder("ImportsApp")
            .AddSource("Service.cs", "internal sealed class Service { }");
        var (workspace, solution) = builder.Build();
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(builder.Directory));
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            file = Path.Combine(builder.ProjectDir, "Missing.cs"),
            solution = builder.SolutionPath
        }));

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var result = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync(RemoveUnusedUsingsCommandDefinition.Definition.Name, arguments.RootElement);

            result.isError.ShouldBeTrue();
            result.content.ShouldContain(nameof(RtErrorKind.NotFound));
            result.content.ShouldContain("Missing.cs");
        }
    }
}
