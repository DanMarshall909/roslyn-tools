using System.Text.Json;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Daemon;

public sealed class DaemonRpcRecoveryTests
{
    [Fact]
    public async Task Missing_daemon_is_started_and_the_rpc_is_retried()
    {
        var expected = new DaemonRpcResponse(
            DaemonRpcProtocol.CurrentVersion,
            "request",
            JsonSerializer.SerializeToElement(new { value = 42 }));
        var rpc = new StubRpcClient(null, expected);
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecovery(rpc, launcher);

        var response = await recovery.TryExecuteAsync(
            "find_callers",
            JsonSerializer.SerializeToElement(new { symbol = "Run" }));

        response.ShouldBeSameAs(expected);
        rpc.CallCount.ShouldBe(2);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_startup_returns_null_for_the_existing_in_process_fallback()
    {
        var rpc = new StubRpcClient((DaemonRpcResponse?)null);
        var launcher = new StubLauncher(started: false);
        var recovery = new DaemonRpcRecovery(rpc, launcher);

        var response = await recovery.TryExecuteAsync(
            "find_callers",
            JsonSerializer.SerializeToElement(new { symbol = "Run" }));

        response.ShouldBeNull();
        rpc.CallCount.ShouldBe(1);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_retry_returns_null_for_the_existing_in_process_fallback()
    {
        var rpc = new StubRpcClient(null, null);
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecovery(rpc, launcher);

        var response = await recovery.TryExecuteAsync(
            "find_callers",
            JsonSerializer.SerializeToElement(new { symbol = "Run" }));

        response.ShouldBeNull();
        rpc.CallCount.ShouldBe(2);
        launcher.CallCount.ShouldBe(1);
    }

    private sealed class StubRpcClient(params DaemonRpcResponse?[] responses) : IDaemonRpcClient
    {
        private readonly Queue<DaemonRpcResponse?> _responses = new(responses);

        internal int CallCount { get; private set; }

        public Task<DaemonRpcResponse?> TryExecuteAsync(
            string commandName,
            JsonElement arguments,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class StubLauncher(bool started) : IMcpDaemonLauncher
    {
        internal int CallCount { get; private set; }

        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(started);
        }
    }
}
