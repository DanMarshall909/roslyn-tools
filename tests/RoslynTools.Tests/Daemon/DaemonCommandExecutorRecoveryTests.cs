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

    [Theory]
    [InlineData("App.slnx")]
    [InlineData(null)]
    public async Task Daemon_dispatch_normalizes_relative_or_omitted_solution_before_the_persistent_boundary(
        string? requestedSolution)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution />");
        var port = new FakeSolutionPort(ReadOnlyDefinition());
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
        var previousDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(root.Path);
            await DaemonCommandExecutor.ExecuteTypedAsync(
                recovery,
                port,
                new FakeSolutionRequest("hello", requestedSolution));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }

        rpc.LastArguments.ShouldNotBeNull();
        rpc.LastArguments.Value.GetProperty("Solution").GetString().ShouldBe(solutionPath);
    }

    [Fact]
    public async Task Invocation_scoped_trust_keeps_read_only_execution_local()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution />");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var port = new FakeSolutionPort(ReadOnlyDefinition());
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

        await DaemonCommandExecutor.ExecuteTypedAsync(
            recovery,
            port,
            new FakeSolutionRequest("hello", solutionPath));

        port.ObservedSolution.ShouldBe(solutionPath);
        rpc.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Doctor_without_an_explicit_solution_stays_local_for_directory_diagnostics()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(root.Path, "Ancestor.slnx"), "<Solution />");
        var requestedDirectory = Directory.CreateDirectory(Path.Combine(root.Path, "empty")).FullName;
        var port = new FakeDoctorPort(ReadOnlyDefinition());
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

        await DaemonCommandExecutor.ExecuteTypedAsync(
            recovery,
            port,
            new DoctorRequest(requestedDirectory));

        port.InvocationCount.ShouldBe(1);
        rpc.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Direct_read_only_execution_receives_the_same_normalized_solution_request()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution />");
        var invocationDirectory = Directory.CreateDirectory(Path.Combine(root.Path, "src")).FullName;
        var previousDirectory = Directory.GetCurrentDirectory();
        using var localOnly = DaemonRuntime.UseExperimentOverride(false);
        var port = new FakeSolutionPort(ReadOnlyDefinition());
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: false));

        try
        {
            Directory.SetCurrentDirectory(invocationDirectory);
            await DaemonCommandExecutor.ExecuteTypedAsync(
                recovery,
                port,
                new FakeSolutionRequest("hello", "../App.slnx"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }

        port.ObservedSolution.ShouldBe(solutionPath);
        rpc.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Pre_dispatch_fallback_receives_the_same_normalized_solution_as_daemon_dispatch()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution />");
        var invocationDirectory = Directory.CreateDirectory(Path.Combine(root.Path, "src")).FullName;
        var previousDirectory = Directory.GetCurrentDirectory();
        var port = new FakeSolutionPort(ReadOnlyDefinition());
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: false));

        try
        {
            Directory.SetCurrentDirectory(invocationDirectory);
            await DaemonCommandExecutor.ExecuteTypedAsync(
                recovery,
                port,
                new FakeSolutionRequest("hello", "../App.slnx"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }

        rpc.LastArguments.ShouldNotBeNull();
        rpc.LastArguments.Value.GetProperty("Solution").GetString().ShouldBe(solutionPath);
        port.ObservedSolution.ShouldBe(solutionPath);
    }

    [Fact]
    public async Task Direct_CLI_outline_keeps_relative_path_targets_invocation_relative()
    {
        using var root = new TemporaryDirectory();
        var invocationDirectory = Directory.CreateDirectory(Path.Combine(root.Path, "src")).FullName;
        var previousDirectory = Directory.GetCurrentDirectory();
        using var localOnly = DaemonRuntime.UseExperimentOverride(false);
        var port = new FakeOutlinePort(ReadOnlyDefinition());
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: false));

        try
        {
            Directory.SetCurrentDirectory(invocationDirectory);
            await DaemonCommandExecutor.ExecuteTypedAsync(
                recovery,
                port,
                new OutlineRequest("Widget.cs", Solution: Path.Combine(root.Path, "App.slnx")));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }

        port.ObservedTarget.ShouldBe(Path.Combine(invocationDirectory, "Widget.cs"));
        rpc.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Daemon_CLI_outline_normalizes_solution_and_target_from_the_invocation_directory()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution />");
        var invocationDirectory = Directory.CreateDirectory(Path.Combine(root.Path, "src")).FullName;
        var previousDirectory = Directory.GetCurrentDirectory();
        var port = new FakeOutlinePort(ReadOnlyDefinition());
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

        try
        {
            Directory.SetCurrentDirectory(invocationDirectory);
            await DaemonCommandExecutor.ExecuteTypedAsync(
                recovery,
                port,
                new OutlineRequest("Widget.cs", Solution: "../App.slnx"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }

        rpc.LastArguments.ShouldNotBeNull();
        rpc.LastArguments.Value.GetProperty("Target").GetString()
            .ShouldBe(Path.Combine(invocationDirectory, "Widget.cs"));
        rpc.LastArguments.Value.GetProperty("Solution").GetString().ShouldBe(solutionPath);
    }

    [Fact]
    public async Task Mutation_solution_arguments_remain_on_the_local_path_without_daemon_normalization()
    {
        var port = new FakeSolutionPort(MutationDefinition());
        var rpc = new StubRpcClient(new DaemonRpcAttempt(
            DaemonRpcAttemptState.IndeterminateAfterDispatch,
            null,
            DispatchStarted: true));
        var recovery = new DaemonRpcRecoveryCoordinator(rpc, new StubLauncher(started: true));

        await DaemonCommandExecutor.ExecuteTypedAsync(
            recovery,
            port,
            new FakeSolutionRequest("hello", "relative/App.slnx"));

        port.ObservedSolution.ShouldBe("relative/App.slnx");
        rpc.CallCount.ShouldBe(0);
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
    private sealed record FakeSolutionRequest(string Value, string? Solution);
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

    private sealed class FakeSolutionPort(ApplicationCommandDefinition definition)
        : CommandApplicationPort<FakeSolutionRequest, FakeResponse>
    {
        internal string? ObservedSolution { get; private set; }
        public override ApplicationCommandDefinition Definition => definition;

        public override Task<Result<FakeResponse, RtError>> ExecuteAsync(
            FakeSolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ObservedSolution = request.Solution;
            return Task.FromResult(Result<FakeResponse, RtError>.Ok(new FakeResponse(request.Value)));
        }
    }

    private sealed class FakeDoctorPort(ApplicationCommandDefinition definition)
        : CommandApplicationPort<DoctorRequest, FakeResponse>
    {
        internal int InvocationCount { get; private set; }
        public override ApplicationCommandDefinition Definition => definition;

        public override Task<Result<FakeResponse, RtError>> ExecuteAsync(
            DoctorRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(Result<FakeResponse, RtError>.Ok(new FakeResponse(request.Directory)));
        }
    }

    private sealed class FakeOutlinePort(ApplicationCommandDefinition definition)
        : CommandApplicationPort<OutlineRequest, FakeResponse>
    {
        internal string? ObservedTarget { get; private set; }
        public override ApplicationCommandDefinition Definition => definition;

        public override Task<Result<FakeResponse, RtError>> ExecuteAsync(
            OutlineRequest request,
            CancellationToken cancellationToken = default)
        {
            ObservedTarget = request.Target;
            return Task.FromResult(Result<FakeResponse, RtError>.Ok(new FakeResponse(request.Target ?? string.Empty)));
        }
    }

    private sealed class StubRpcClient(DaemonRpcAttempt attempt) : IDaemonRpcClient
    {
        internal int CallCount { get; private set; }
        internal JsonElement? LastArguments { get; private set; }

        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastArguments = arguments.Clone();
            return Task.FromResult(attempt);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"rt-daemon-executor-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
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
