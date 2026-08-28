using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using RoslynTools.Tests;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public sealed class ExtractClassToolTests
{
    [Fact]
    public void generic_mcp_projects_the_typed_extract_class_schema()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == ExtractClassCommandDefinition.Definition.Name);
        var tool = ToolSchemaBuilder.Build(descriptor);

        descriptor.Port.ShouldBeOfType<ExtractClassOperation>();
        descriptor.Definition.ShouldBeSameAs(ExtractClassCommandDefinition.Definition);
        ((string[])tool.inputSchema["required"]).ShouldBe(["type", "members", "new-type"], ignoreOrder: true);
        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];
        properties.Keys.ShouldBe(
            ["type", "members", "new-type", "static", "output-dir", "solution", "dry-run"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task generic_mcp_extract_class_defaults_to_preview_and_returns_the_typed_result()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse("""
                                                   {"type":"Widget","members":"Format","new-type":"WidgetFormatter","solution":"ignored.slnx"}
                                                   """))
        {
            var result = await CreateAdapter(provider).InvokeAsync("extract_class", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            payload.RootElement.GetProperty("value").GetProperty("newType").GetString()
                .ShouldBe("WidgetFormatter");
            payload.RootElement.GetProperty("changedFiles").GetArrayLength().ShouldBe(2);
            project.SourceExists("WidgetFormatter.cs").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task generic_mcp_extract_class_returns_a_typed_invalid_member_error()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse("""
                                                   {"type":"Widget","members":"Missing","new-type":"WidgetFormatter","solution":"ignored.slnx"}
                                                   """))
        {
            var result = await CreateAdapter(provider).InvokeAsync("extract_class", arguments.RootElement);

            result.isError.ShouldBeTrue();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
            payload.RootElement.GetProperty("message").GetString()!
                .ShouldContain("None of the specified members (Missing) were found", Case.Sensitive);
        }
    }

    private static AdhocProjectBuilder CreateProject()
    {
        var project = new AdhocProjectBuilder("ExtractClassApp");
        project.AddSource("Widget.cs", """
                                       namespace TestApp;
                                       public class Widget { public void Format() { } }
                                       """);
        return project;
    }

    private static McpCommandAdapter CreateAdapter(IServiceProvider provider) => new(
        provider.GetRequiredService<CommandDescriptorRegistry>(),
        provider.GetRequiredService<CliCommandBridge>(),
        new DaemonRpcRecoveryCoordinator(new UnavailableRpcClient(), new UnavailableLauncher()));

    private sealed class UnavailableRpcClient : IDaemonRpcClient
    {
        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default) => Task.FromResult(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
    }

    private sealed class UnavailableLauncher : IDaemonLauncher
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
