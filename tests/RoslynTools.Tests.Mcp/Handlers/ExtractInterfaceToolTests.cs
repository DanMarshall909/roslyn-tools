using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Tools;
using RoslynTools.Tests;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public sealed class ExtractInterfaceToolTests
{
    [Fact]
    public async Task extract_interface_defaults_to_dry_run_and_returns_a_structured_result()
    {
        using var project = new AdhocProjectBuilder("InterfaceApp");
        project.AddSource("Widget.cs", """
                                        namespace TestApp;
                                        public class Widget { public void Run() { } }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse("""{"type":"Widget","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider)
                .InvokeAsync("extract_interface", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            payload.RootElement.GetProperty("value").GetProperty("interface").GetString()
                .ShouldBe("IWidget");
            payload.RootElement.GetProperty("changedFiles").GetArrayLength().ShouldBe(2);
            project.SourceExists("IWidget.cs").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task extract_interface_returns_an_actionable_typed_not_found_error()
    {
        using var project = new AdhocProjectBuilder("InterfaceApp");
        project.AddSource("Widget.cs", "namespace TestApp; public class Widget { }");
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse("""{"type":"Missing","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider)
                .InvokeAsync("extract_interface", arguments.RootElement);

            result.isError.ShouldBeTrue();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.NotFound));
            payload.RootElement.GetProperty("message").GetString()!
                .ShouldContain("No symbol named 'Missing'", Case.Sensitive);
        }
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
