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
public sealed class MoveMethodToolTests
{
    [Fact]
    public async Task move_method_defaults_to_dry_run_and_returns_a_structured_result()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"method-fqn":"App.Source.Add","to-type":"App.Target","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider)
                .InvokeAsync("move_method", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            payload.RootElement.GetProperty("value").GetProperty("methodName").GetString()
                .ShouldBe("Add");
            payload.RootElement.GetProperty("value").GetProperty("callSiteCount").GetInt32()
                .ShouldBe(1);
            (await project.ReadSourceAsync("Source.cs")).ShouldContain("Add", Case.Sensitive);
        }
    }

    [Fact]
    public async Task move_method_returns_an_actionable_typed_missing_target_error()
    {
        using var project = new AdhocProjectBuilder("MoveApp");
        project.AddSource("Source.cs", "namespace App; public static class Source { public static int Add(int x) => x; }");
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"method-fqn":"App.Source.Add","to-type":"App.Missing","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider)
                .InvokeAsync("move_method", arguments.RootElement);

            result.isError.ShouldBeTrue();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.NotFound));
            payload.RootElement.GetProperty("message").GetString()!
                .ShouldContain("Pass --create-file", Case.Sensitive);
            payload.RootElement.GetProperty("context").GetProperty("to-type").GetString()
                .ShouldBe("App.Missing");
        }
    }

    [Fact]
    public async Task move_method_returns_an_actionable_typed_invalid_target_error()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"method-fqn":"App.Source.Add","to-type":"","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider)
                .InvokeAsync("move_method", arguments.RootElement);

            result.isError.ShouldBeTrue();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
            payload.RootElement.GetProperty("message").GetString()!
                .ShouldContain("to-type is required", Case.Sensitive);
        }
    }

    private static AdhocProjectBuilder CreateProject()
    {
        var project = new AdhocProjectBuilder("MoveApp");
        project.AddSource("Source.cs", "namespace App; public static class Source { public static int Add(int x) => x; }");
        project.AddSource("Target.cs", "namespace App; public static class Target { }");
        project.AddSource("Caller.cs", "namespace App; public class Caller { public int Run() => Source.Add(1); }");
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
