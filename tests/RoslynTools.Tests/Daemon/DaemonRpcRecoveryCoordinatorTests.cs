using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonRpcRecoveryCoordinatorTests
{
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { value = 1 });

    [Fact]
    public async Task Unavailable_before_dispatch_starts_and_retries_once()
    {
        var response = CompletedResponse();
        var rpc = new StubRpcClient(
            new DaemonRpcAttempt(DaemonRpcAttemptState.UnavailableBeforeDispatch, null, DispatchStarted: false),
            new DaemonRpcAttempt(DaemonRpcAttemptState.Completed, response, DispatchStarted: true));
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteAsync(
            OutlineCommandDefinition.Definition,
            Arguments,
            CancellationToken.None);

        result.Response.ShouldBeSameAs(response);
        result.ShouldFallback.ShouldBeFalse();
        result.LastAttempt.State.ShouldBe(DaemonRpcAttemptState.Completed);
        rpc.CallCount.ShouldBe(2);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Indeterminate_mutation_is_never_retried_or_fallen_back()
    {
        var attempt = new DaemonRpcAttempt(
            DaemonRpcAttemptState.IndeterminateAfterDispatch,
            null,
            DispatchStarted: true);
        var rpc = new StubRpcClient(attempt);
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteAsync(
            AddDependencyCommandDefinition.Definition,
            Arguments,
            CancellationToken.None);

        result.Response.ShouldBeNull();
        result.ShouldFallback.ShouldBeFalse();
        result.LastAttempt.ShouldBe(attempt);
        rpc.CallCount.ShouldBe(1);
        launcher.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Read_only_indeterminate_attempt_may_recover_then_fall_back_safely()
    {
        var rpc = new StubRpcClient(
            new DaemonRpcAttempt(
                DaemonRpcAttemptState.IndeterminateAfterDispatch,
                null,
                DispatchStarted: true),
            new DaemonRpcAttempt(
                DaemonRpcAttemptState.UnavailableBeforeDispatch,
                null,
                DispatchStarted: false));
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteAsync(
            OutlineCommandDefinition.Definition,
            Arguments,
            CancellationToken.None);

        result.Response.ShouldBeNull();
        result.ShouldFallback.ShouldBeTrue();
        result.LastAttempt.State.ShouldBe(DaemonRpcAttemptState.UnavailableBeforeDispatch);
        rpc.CallCount.ShouldBe(2);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_startup_allows_safe_in_process_fallback()
    {
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
        var launcher = new StubLauncher(started: false);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteAsync(
            AddDependencyCommandDefinition.Definition,
            Arguments);

        result.Response.ShouldBeNull();
        result.ShouldFallback.ShouldBeTrue();
        rpc.CallCount.ShouldBe(1);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Mutation_unavailable_before_dispatch_may_start_and_retry()
    {
        var response = CompletedResponse();
        var rpc = new StubRpcClient(
            new DaemonRpcAttempt(
                DaemonRpcAttemptState.UnavailableBeforeDispatch,
                null,
                DispatchStarted: false),
            new DaemonRpcAttempt(
                DaemonRpcAttemptState.Completed,
                response,
                DispatchStarted: true));
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteAsync(
            AddDependencyCommandDefinition.Definition,
            Arguments);

        result.Response.ShouldBeSameAs(response);
        result.ShouldFallback.ShouldBeFalse();
        rpc.CallCount.ShouldBe(2);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Mutation_retry_that_becomes_indeterminate_is_terminal()
    {
        var rpc = new StubRpcClient(
            new DaemonRpcAttempt(
                DaemonRpcAttemptState.UnavailableBeforeDispatch,
                null,
                DispatchStarted: false),
            new DaemonRpcAttempt(
                DaemonRpcAttemptState.IndeterminateAfterDispatch,
                null,
                DispatchStarted: true));
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        var result = await recovery.ExecuteAsync(
            AddDependencyCommandDefinition.Definition,
            Arguments);

        result.Response.ShouldBeNull();
        result.ShouldFallback.ShouldBeFalse();
        result.LastAttempt.State.ShouldBe(DaemonRpcAttemptState.IndeterminateAfterDispatch);
        rpc.CallCount.ShouldBe(2);
        launcher.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_into_recovery_or_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var rpc = new CancellingRpcClient();
        var launcher = new StubLauncher(started: true);
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, launcher);

        await Should.ThrowAsync<OperationCanceledException>(() => recovery.ExecuteAsync(
            OutlineCommandDefinition.Definition,
            Arguments,
            cancellation.Token));

        launcher.CallCount.ShouldBe(0);
    }

    [Fact]
    public void Mutation_cancellation_after_dispatch_is_not_reported_as_clean_cancellation()
    {
        DaemonRpcClient.ShouldPreserveCallerCancellation(
                dispatchStarted: true,
                CommandSafety.ReviewByDefault)
            .ShouldBeFalse();
        DaemonRpcClient.ShouldPreserveCallerCancellation(
                dispatchStarted: false,
                CommandSafety.ReviewByDefault)
            .ShouldBeTrue();
        DaemonRpcClient.ShouldPreserveCallerCancellation(
                dispatchStarted: true,
                CommandSafety.ReadOnly)
            .ShouldBeTrue();
    }

    [Fact]
    public void Attempt_states_encode_whether_dispatch_may_have_happened()
    {
        new DaemonRpcAttempt(
                DaemonRpcAttemptState.UnavailableBeforeDispatch,
                null,
                DispatchStarted: false)
            .DispatchStarted.ShouldBeFalse();
        new DaemonRpcAttempt(
                DaemonRpcAttemptState.IndeterminateAfterDispatch,
                null,
                DispatchStarted: true)
            .DispatchStarted.ShouldBeTrue();
        new DaemonRpcAttempt(
                DaemonRpcAttemptState.Completed,
                CompletedResponse(),
                DispatchStarted: true)
            .DispatchStarted.ShouldBeTrue();
    }

    [Fact]
    public async Task Client_marks_a_lost_response_after_writing_as_indeterminate()
    {
        var pipeName = $"rt-recovery-test-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, new UTF8Encoding(false), leaveOpen: true);
            (await reader.ReadLineAsync()).ShouldNotBeNull();
            server.Disconnect();
        });
        var client = new DaemonRpcClient(pipeName);

        var attempt = await client.ExecuteAsync(
            "outline",
            Arguments,
            CommandSafety.ReadOnly);
        await serverTask;

        attempt.State.ShouldBe(DaemonRpcAttemptState.IndeterminateAfterDispatch);
        attempt.DispatchStarted.ShouldBeTrue();
        attempt.Response.ShouldBeNull();
    }

    [Fact]
    public async Task Client_marks_a_malformed_response_after_writing_as_indeterminate()
    {
        var pipeName = $"rt-recovery-test-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, new UTF8Encoding(false), leaveOpen: true);
            (await reader.ReadLineAsync()).ShouldNotBeNull();
            await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            await writer.WriteLineAsync("not-json");
        });
        var client = new DaemonRpcClient(pipeName);

        var attempt = await client.ExecuteAsync(
            "outline",
            Arguments,
            CommandSafety.ReadOnly);
        await serverTask;

        attempt.State.ShouldBe(DaemonRpcAttemptState.IndeterminateAfterDispatch);
        attempt.DispatchStarted.ShouldBeTrue();
        attempt.Response.ShouldBeNull();
    }

    private static DaemonRpcResponse CompletedResponse() => new(
        DaemonRpcProtocol.CurrentVersion,
        "request",
        JsonSerializer.SerializeToElement(new { value = 42 }));

    private sealed class StubRpcClient(params DaemonRpcAttempt[] attempts) : IDaemonRpcClient
    {
        private readonly Queue<DaemonRpcAttempt> _attempts = new(attempts);

        internal int CallCount { get; private set; }

        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_attempts.Dequeue());
        }
    }

    private sealed class CancellingRpcClient : IDaemonRpcClient
    {
        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default) =>
            Task.FromCanceled<DaemonRpcAttempt>(cancellationToken);
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
