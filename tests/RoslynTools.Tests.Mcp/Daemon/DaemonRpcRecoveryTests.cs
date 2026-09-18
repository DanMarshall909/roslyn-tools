using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
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
        var rpc = new StubRpcClient(Unavailable(), Completed(expected));
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteLegacyAsync(
            "find_callers",
            JsonSerializer.SerializeToElement(new { symbol = "Run" }),
            CommandSafety.ReadOnly);

        result.Response.ShouldBeSameAs(expected);
        result.ShouldFallback.ShouldBeFalse();
        rpc.CallCount.ShouldBe(2);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_startup_returns_null_for_the_existing_in_process_fallback()
    {
        var rpc = new StubRpcClient(Unavailable());
        var launcher = new StubLauncher(started: false);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteLegacyAsync(
            "find_callers",
            JsonSerializer.SerializeToElement(new { symbol = "Run" }),
            CommandSafety.ReadOnly);

        result.Response.ShouldBeNull();
        result.ShouldFallback.ShouldBeTrue();
        rpc.CallCount.ShouldBe(1);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_retry_returns_null_for_the_existing_in_process_fallback()
    {
        var rpc = new StubRpcClient(Unavailable(), Unavailable());
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteLegacyAsync(
            "find_callers",
            JsonSerializer.SerializeToElement(new { symbol = "Run" }),
            CommandSafety.ReadOnly);

        result.Response.ShouldBeNull();
        result.ShouldFallback.ShouldBeTrue();
        rpc.CallCount.ShouldBe(2);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Disabled_auto_start_falls_back_without_launching_a_missing_daemon()
    {
        using var configuration = DaemonRuntime.UseConfiguration(new RtDaemonSettings(
            Enabled: true,
            AutoStart: false,
            TimeSpan.FromMinutes(10)));
        var rpc = new StubRpcClient(Unavailable());
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteLegacyAsync(
            "find_callers",
            JsonSerializer.SerializeToElement(new { symbol = "Run" }),
            CommandSafety.ReadOnly);

        result.Response.ShouldBeNull();
        result.ShouldFallback.ShouldBeTrue();
        rpc.CallCount.ShouldBe(1);
        launcher.CallCount.ShouldBe(0);
    }

    private static DaemonRpcAttempt Unavailable() => new(
        DaemonRpcAttemptState.UnavailableBeforeDispatch,
        null,
        DispatchStarted: false);

    private static DaemonRpcAttempt Completed(DaemonRpcResponse response) => new(
        DaemonRpcAttemptState.Completed,
        response,
        DispatchStarted: true);

    private sealed class StubRpcClient(params DaemonRpcAttempt[] responses) : IDaemonRpcClient
    {
        private readonly Queue<DaemonRpcAttempt> _responses = new(responses);

        internal int CallCount { get; private set; }

        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class StubLauncher(bool started) : IDaemonLauncher
    {
        internal int CallCount { get; private set; }

        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(started);
        }
    }
}
