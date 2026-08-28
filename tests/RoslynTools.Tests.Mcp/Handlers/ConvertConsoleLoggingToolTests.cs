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
public sealed class ConvertConsoleLoggingToolTests
{
    [Fact]
    public void descriptor_projects_the_review_by_default_typed_port()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == "convert_console_logging");
        var tool = ToolSchemaBuilder.Build(descriptor);

        descriptor.Port.ShouldBeOfType<ConvertConsoleLoggingOperation>();
        descriptor.Definition.ShouldBeSameAs(ConvertConsoleLoggingCommandDefinition.Definition);
        descriptor.Definition!.Safety.ShouldBe(CommandSafety.ReviewByDefault);
        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];
        properties.Keys.ShouldContain("stream");
        properties.Keys.ShouldContain("inject-logger");
        properties.Keys.ShouldContain("dry-run");
    }

    [Fact]
    public async Task generic_mcp_defaults_the_mutation_to_a_non_writing_preview()
    {
        using var project = BuildProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"solution":"ignored.slnx","stream":"all"}"""))
        {
            var result = await CreateAdapter(provider)
                .InvokeAsync("convert_console_logging", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            payload.RootElement.GetProperty("value").GetProperty("entryCount").GetInt32().ShouldBe(1);
            (await project.ReadSourceAsync("LogApp.cs"))
                .ShouldContain("Console.Error.WriteLine", Case.Sensitive);
        }
    }

    [Fact]
    public async Task explicit_review_returns_structured_evidence_without_writing()
    {
        using var project = BuildProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"solution":"ignored.slnx","stream":"all","review":true,"dry-run":false}"""))
        {
            var result = await CreateAdapter(provider)
                .InvokeAsync("convert_console_logging", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.Reviewed);
            payload.RootElement.GetProperty("value").GetProperty("entries").GetArrayLength().ShouldBe(1);
            (await project.ReadSourceAsync("LogApp.cs"))
                .ShouldContain("Console.Error.WriteLine", Case.Sensitive);
        }
    }

    [Fact]
    public async Task invalid_stream_returns_a_typed_error_before_workspace_loading()
    {
        using var provider = McpTestHost.CreateProvider();
        using var arguments = JsonDocument.Parse("""{"stream":"console"}""");

        var result = await CreateAdapter(provider)
            .InvokeAsync("convert_console_logging", arguments.RootElement);

        result.isError.ShouldBeTrue();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
        payload.RootElement.GetProperty("message").GetString()
            .ShouldNotBeNull()
            .ShouldContain("all, stdout, stderr", Case.Sensitive);
    }

    private static AdhocProjectBuilder BuildProject()
    {
        var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                          class App
                                          {
                                              void Run() => Console.Error.WriteLine("Starting");
                                          }
                                          """);
        return project;
    }

    private static McpCommandAdapter CreateAdapter(IServiceProvider provider)
    {
        return new McpCommandAdapter(
            provider.GetRequiredService<CommandDescriptorRegistry>(),
            provider.GetRequiredService<CliCommandBridge>(),
            new DaemonRpcRecoveryCoordinator(new UnavailableRpcClient(), new UnavailableLauncher()));
    }

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
