using System.Globalization;
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
public sealed class SafeDeleteToolTests
{
    [Fact]
    public async Task safe_delete_defaults_to_dry_run_and_returns_the_typed_mutation_envelope()
    {
        using var project = new AdhocProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", "public class Orphan { }");
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"symbol":"Orphan","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider)
                .InvokeAsync("safe_delete", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            payload.RootElement.GetProperty("changedFiles").GetArrayLength().ShouldBe(1);
            payload.RootElement.GetProperty("value").GetProperty("referenceCount").GetInt32().ShouldBe(0);
            var content = await project.ReadSourceAsync("SafeApp.cs");
            content.ShouldContain("class Orphan", Case.Sensitive);
        }
    }

    [Fact]
    public async Task safe_delete_returns_a_typed_error_with_reference_evidence()
    {
        using var project = new AdhocProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", """
                                         public class Used { }
                                         public class Caller { private readonly Used _used = new(); }
                                         """);
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"symbol":"Used","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider)
                .InvokeAsync("safe_delete", arguments.RootElement);

            result.isError.ShouldBeTrue();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
            var context = payload.RootElement.GetProperty("context");
            int.Parse(context.GetProperty("referenceCount").GetString()!, CultureInfo.InvariantCulture)
                .ShouldBeGreaterThan(0);
            context.GetProperty("reference.0.filePath").GetString()
                .ShouldEndWith("SafeApp.cs", Case.Sensitive);
            var content = await project.ReadSourceAsync("SafeApp.cs");
            content.ShouldContain("class Used", Case.Sensitive);
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
