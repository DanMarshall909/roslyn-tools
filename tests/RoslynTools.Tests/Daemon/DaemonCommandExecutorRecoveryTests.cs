using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonCommandExecutorRecoveryTests : IDisposable
{
    private readonly IDisposable _daemonExperiment = DaemonRuntime.UseExperimentOverride(true);

    public void Dispose() => _daemonExperiment.Dispose();

    [Fact]
    public async Task Read_only_tracer_falls_back_through_the_same_port_with_daemon_shape_parity()
    {
        var port = new FakePort(ReadOnlyDefinition());
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: false));
        var context = CreateInvocationContext();
        var console = new CapturingConsole();
        using var capture = RtConsole.UseConsole(console);

        await DaemonCommandExecutor.ExecuteAsync(
            recovery,
            port,
            new FakeRequest("hello"),
            context);

        context.ExitCode.ShouldBe(0);
        port.InvocationCount.ShouldBe(1);
        console.Output.ShouldContain("\"data\"", Case.Sensitive);
        console.Output.ShouldContain("\"suggestedTools\"", Case.Sensitive);
        console.Output.ShouldNotContain("\"Data\"", Case.Sensitive);
    }

    [Fact]
    public async Task Mutation_executor_runs_in_process_without_daemon_dispatch()
    {
        var port = new FakePort(MutationDefinition());
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.IndeterminateAfterDispatch,
            null,
            DispatchStarted: true));
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: true));
        var context = CreateInvocationContext();
        var console = new CapturingConsole();
        using var capture = RtConsole.UseConsole(console);

        await DaemonCommandExecutor.ExecuteAsync(
            recovery,
            port,
            new FakeRequest("hello"),
            context);

        context.ExitCode.ShouldBe(0);
        port.InvocationCount.ShouldBe(1);
        rpc.CallCount.ShouldBe(0);
        console.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Typed_execution_deserializes_the_domain_response_before_CLI_projection()
    {
        var port = new FakePort(ReadOnlyDefinition());
        var response = new DaemonRpcResponse(
            DaemonRpcProtocol.CurrentVersion,
            "request",
            DaemonRpcJson.SerializeExecution(new CommandExecutionResult(
                new FakeResponse("from daemon"),
                [])));
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.Completed,
            response,
            DispatchStarted: true));
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: false));

        var result = await DaemonCommandExecutor.ExecuteTypedAsync(
            recovery,
            port,
            new FakeRequest("hello"));

        result.Match(
            value => value.Value.ShouldBe("from daemon"),
            error => throw new ShouldAssertException(error.Message));
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Typed_execution_returns_daemon_errors_without_local_retry()
    {
        var port = new FakePort(ReadOnlyDefinition());
        var response = new DaemonRpcResponse(
            DaemonRpcProtocol.CurrentVersion,
            "request",
            Error: new DaemonRpcError(
                DaemonRpcErrorCodes.CommandFailed,
                "bad request",
                RtErrorKind.InvalidInput));
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.Completed,
            response,
            DispatchStarted: true));
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: false));

        var result = await DaemonCommandExecutor.ExecuteTypedAsync(
            recovery,
            port,
            new FakeRequest("hello"));

        result.Match(
            _ => throw new ShouldAssertException("Expected an error result."),
            error =>
            {
                error.Kind.ShouldBe(RtErrorKind.InvalidInput);
                error.Message.ShouldBe("bad request");
            });
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Typed_execution_falls_back_when_a_resident_daemon_is_older_than_the_command()
    {
        var port = new FakePort(ReadOnlyDefinition());
        var response = new DaemonRpcResponse(
            DaemonRpcProtocol.CurrentVersion,
            "request",
            Error: new DaemonRpcError(
                DaemonRpcErrorCodes.CommandNotFound,
                "unknown command"));
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.Completed,
            response,
            DispatchStarted: true));
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: false));

        var result = await DaemonCommandExecutor.ExecuteTypedAsync(
            recovery,
            port,
            new FakeRequest("local fallback"));

        result.Match(
            value => value.Value.ShouldBe("local fallback"),
            error => throw new ShouldAssertException(error.Message));
        port.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Typed_execution_is_local_when_the_preview_daemon_experiment_is_disabled()
    {
        using (DaemonRuntime.UseExperimentOverride(false))
        {
            var port = new FakePort(ReadOnlyDefinition());
            var rpc = new StubRpcClient(new DaemonRpcAttempt(
                DaemonRpcAttemptState.UnavailableBeforeDispatch,
                null,
                DispatchStarted: false));
            var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: true));

            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                recovery,
                port,
                new FakeRequest("local only"));

            result.Match(
                value => value.Value.ShouldBe("local only"),
                error => throw new ShouldAssertException(error.Message));
            port.InvocationCount.ShouldBe(1);
            rpc.CallCount.ShouldBe(0);
        }
    }

    private static InvocationContext CreateInvocationContext() =>
        new(new RootCommand().Parse([]));

    private static ApplicationCommandDefinition ReadOnlyDefinition() => new(
        new RtCommandInfo("test-read", false, true, "inspect", "Test.", []),
        typeof(FakeRequest),
        typeof(FakeResponse),
        mcpVisible: true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        [new CommandFollowOn("describe", "Inspect.")]);

    private static ApplicationCommandDefinition MutationDefinition() => new(
        new RtCommandInfo("test-write", true, true, "refactor", "Test.", []),
        typeof(FakeRequest),
        typeof(FakeResponse),
        mcpVisible: false,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);

    private sealed record FakeRequest(string Value);
    private sealed record FakeResponse(string Value);

    private sealed class FakePort(ApplicationCommandDefinition definition)
        : CommandApplicationPort<FakeRequest, FakeResponse>
    {
        internal int InvocationCount { get; private set; }
        public override ApplicationCommandDefinition Definition => definition;

        public override Task<Result<FakeResponse, RtError>> ExecuteAsync(
            FakeRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(Result<FakeResponse, RtError>.Ok(new FakeResponse(request.Value)));
        }
    }

    private sealed class StubRpcClient(DaemonRpcAttempt attempt) : IDaemonRpcClient
    {
        internal int CallCount { get; private set; }

        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(attempt);
        }
    }

    private sealed class StubLauncher(bool started) : IDaemonLauncher
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(started);
    }

    private sealed class CapturingConsole : IRtConsole
    {
        internal string Output => string.Join(Environment.NewLine, OutputLines);
        internal List<string> OutputLines { get; } = [];
        internal List<string> Errors { get; } = [];
        public bool IsOutputRedirected => true;

        public void Log(RtLogLevel level, string template, object?[] args)
        {
            if (level == RtLogLevel.Error)
                Errors.Add(RtConsole.RenderTemplate(template, args));
        }

        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null) =>
            OutputLines.Add(message);

        public void Write(RtOutputContext context, string message, ConsoleColor? color = null) =>
            OutputLines.Add(message);
    }
}
