using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonCommandExecutorRecoveryTests
{
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
    public async Task Mutation_tracer_does_not_fall_back_after_indeterminate_dispatch()
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

        context.ExitCode.ShouldBe(1);
        port.InvocationCount.ShouldBe(0);
        console.Errors.ShouldContain(message => message.Contains("indeterminate", StringComparison.Ordinal));
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
        mcpVisible: true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.DaemonPreferred,
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
        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default) => Task.FromResult(attempt);
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
