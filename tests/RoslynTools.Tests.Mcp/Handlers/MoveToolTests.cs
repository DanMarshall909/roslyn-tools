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
public sealed class MoveToolTests
{
    [Fact]
    public async Task move_defaults_to_dry_run_through_the_generic_adapter()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"mode":"TypeToNamespace","type":"App.Old.Widget","to-namespace":"App.New","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider).InvokeAsync("move", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            payload.RootElement.GetProperty("value").GetProperty("targetNamespace").GetString()
                .ShouldBe("App.New");
            (await project.ReadSourceAsync("Widget.cs")).ShouldContain("namespace App.Old;", Case.Sensitive);
        }
    }

    [Fact]
    public async Task move_explicit_apply_writes_the_prepared_change_through_the_generic_adapter()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"mode":"TypeToNamespace","type":"App.Old.Widget","to-namespace":"App.New","dry-run":false,"solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider).InvokeAsync("move", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.Applied);
            (await project.ReadSourceAsync("Widget.cs")).ShouldContain("namespace App.New;", Case.Sensitive);
        }
    }

    [Fact]
    public async Task move_returns_a_typed_invalid_request_error_through_the_generic_adapter()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"mode":"TypeToNamespace","to-namespace":"App.New","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider).InvokeAsync("move", arguments.RootElement);

            result.isError.ShouldBeTrue();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
            payload.RootElement.GetProperty("message").GetString()!
                .ShouldContain("requires type", Case.Sensitive);
        }
    }

    private static AdhocProjectBuilder CreateProject() =>
        new AdhocProjectBuilder("MoveApp")
            .AddSource("Widget.cs", "namespace App.Old; public class Widget { }");

    private static McpCommandAdapter CreateAdapter(IServiceProvider provider) =>
        new(
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
