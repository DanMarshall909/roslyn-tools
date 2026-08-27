using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Daemon;

public sealed class DaemonRecoveryAdapterTests
{
    [Fact]
    public async Task Indeterminate_mutation_is_not_retried_or_fallen_back_by_mcp()
    {
        using var provider = McpTestHost.CreateProvider();
        var rpc = new IndeterminateRpcClient();
        var launcher = new CountingLauncher();
        var adapter = new McpCommandAdapter(
            provider.GetRequiredService<RoslynTools.CommandDescriptors.CommandDescriptorRegistry>(),
            provider.GetRequiredService<CliCommandBridge>(),
            new DaemonRpcRecoveryCoordinator(rpc, launcher));
        using var arguments = JsonDocument.Parse(
            """
            {
              "type": "AgentService",
              "dependency": "IEmailService",
              "dry-run": false
            }
            """);

        var result = await adapter.InvokeAsync(
            AddDependencyCommandDefinition.Name,
            arguments.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("indeterminate", Case.Insensitive);
        rpc.CallCount.ShouldBe(1);
        launcher.CallCount.ShouldBe(0);
    }

    private sealed class IndeterminateRpcClient : IDaemonRpcClient
    {
        internal int CallCount { get; private set; }

        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new DaemonRpcAttempt(
                DaemonRpcAttemptState.IndeterminateAfterDispatch,
                null,
                DispatchStarted: true));
        }
    }

    private sealed class CountingLauncher : IDaemonLauncher
    {
        internal int CallCount { get; private set; }

        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(true);
        }
    }
}
