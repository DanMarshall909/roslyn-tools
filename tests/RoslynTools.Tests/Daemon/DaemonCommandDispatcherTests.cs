using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonCommandDispatcherTests
{
    [Fact]
    public async Task Execute_deserializes_and_invokes_the_typed_application_port()
    {
        var port = new EchoPort();
        var dispatcher = new DaemonCommandDispatcher([port]);
        using var payload = JsonDocument.Parse("""{"value":"hello"}""");
        var request = new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "request-1",
            DaemonRpcProtocol.ExecuteCommandMethod,
            "echo",
            payload.RootElement.Clone());

        var response = await dispatcher.ExecuteAsync(request);

        response.Error.ShouldBeNull();
        response.Result.ShouldNotBeNull();
        response.Result.Value.GetProperty("data").GetProperty("value").GetString().ShouldBe("HELLO");
        response.Result.Value.GetProperty("suggestedTools").GetArrayLength().ShouldBe(0);
        port.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Execute_rejects_unknown_commands_as_structured_errors()
    {
        var dispatcher = new DaemonCommandDispatcher([]);
        using var payload = JsonDocument.Parse("{}");

        var response = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "request-2",
            DaemonRpcProtocol.ExecuteCommandMethod,
            "missing",
            payload.RootElement.Clone()));

        response.Result.ShouldBeNull();
        response.Error.ShouldNotBeNull();
        response.Error.Code.ShouldBe(DaemonRpcErrorCodes.CommandNotFound);
    }

    [Fact]
    public async Task Execute_rejects_incompatible_protocol_versions_before_dispatch()
    {
        var port = new EchoPort();
        var dispatcher = new DaemonCommandDispatcher([port]);
        using var payload = JsonDocument.Parse("{}");

        var response = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            999,
            "request-3",
            DaemonRpcProtocol.ExecuteCommandMethod,
            "echo",
            payload.RootElement.Clone()));

        response.Error.ShouldNotBeNull();
        response.Error.Code.ShouldBe(DaemonRpcErrorCodes.IncompatibleProtocol);
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Execute_adds_application_owned_follow_on_tools_before_any_adapter_renders()
    {
        var port = new EchoPort("outline");
        var dispatcher = new DaemonCommandDispatcher([port]);
        using var payload = JsonDocument.Parse("""{"value":"hello"}""");

        var response = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "request-4",
            DaemonRpcProtocol.ExecuteCommandMethod,
            "outline",
            payload.RootElement.Clone()));

        response.Error.ShouldBeNull();
        response.Result.ShouldNotBeNull();
        response.Result!.Value.GetProperty("suggestedTools").GetArrayLength().ShouldBe(2);
        response.Result.Value.GetProperty("suggestedTools")[0]
            .GetProperty("toolName").GetString().ShouldBe("describe");
    }

    [Fact]
    public async Task Execute_matches_underscore_daemon_tool_names_to_dashed_port_names()
    {
        var port = new EchoPort("add-dependency");
        var dispatcher = new DaemonCommandDispatcher([port]);
        using var payload = JsonDocument.Parse("""{"value":"hello"}""");

        var response = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "request-5",
            DaemonRpcProtocol.ExecuteCommandMethod,
            AddDependencyCommandDefinition.Name,
            payload.RootElement.Clone()));

        response.Error.ShouldBeNull();
        port.InvocationCount.ShouldBe(1);
        response.Result!.Value.GetProperty("suggestedTools").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Execute_applies_and_then_restores_the_request_working_directory()
    {
        var originalWorkingDirectory = Directory.GetCurrentDirectory();
        var port = new WorkingDirectoryPort();
        var dispatcher = new DaemonCommandDispatcher([port]);
        using var payload = JsonDocument.Parse("{}");

        var response = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "request-working-directory",
            DaemonRpcProtocol.ExecuteCommandMethod,
            "working-directory",
            payload.RootElement.Clone(),
            Path.GetTempPath()));

        response.Error.ShouldBeNull();
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(port.ObservedWorkingDirectory!)).ShouldBe(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())));
        Directory.GetCurrentDirectory().ShouldBe(originalWorkingDirectory);
    }

    [Fact]
    public async Task Execute_returns_workspace_io_failures_as_command_errors()
    {
        var dispatcher = new DaemonCommandDispatcher([new IoFailurePort()]);
        using var payload = JsonDocument.Parse("{}");

        var response = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "request-io-failure",
            DaemonRpcProtocol.ExecuteCommandMethod,
            "io-failure",
            payload.RootElement.Clone()));

        response.Result.ShouldBeNull();
        response.Error.ShouldNotBeNull();
        response.Error.Code.ShouldBe(DaemonRpcErrorCodes.CommandFailed);
        response.Error.Message.ShouldBe("missing solution");
    }

    [Fact]
    public async Task Typed_port_rejects_the_wrong_request_as_a_structured_boundary_error()
    {
        ICommandApplicationPort port = new EchoPort();

        var result = await port.InvokeAsync(new object());

        result.IsError.ShouldBeTrue();
        result.Value.ShouldBeNull();
        object error = result.Error.ShouldNotBeNull();
        var structuredError = error.ShouldBeOfType<RtError>();
        structuredError.Kind.ShouldBe(RtErrorKind.InvalidInput);
        structuredError.Message.ShouldContain(typeof(EchoRequest).FullName!);
    }

    [Fact]
    public async Task Legacy_ports_preserve_catalogued_follow_on_tools_until_they_migrate()
    {
        var dispatcher = new DaemonCommandDispatcher(
            [new LegacyEchoPort("make-sealed")]);
        using var payload = JsonDocument.Parse("""{"value":"hello"}""");

        var response = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "legacy-follow-ons",
            DaemonRpcProtocol.ExecuteCommandMethod,
            MakeSealedCommandDefinition.Name,
            payload.RootElement.Clone()));

        response.Error.ShouldBeNull();
        response.Result!.Value.GetProperty("suggestedTools").GetArrayLength().ShouldBe(2);
        response.Result.Value.GetProperty("suggestedTools")[0]
            .GetProperty("toolName").GetString().ShouldBe("describe");
    }

    [Fact]
    public void Mutation_definitions_publish_their_typed_transport_contracts()
    {
        var addDependency = DaemonOwnedCommands.All
            .Single(command => command.Name == AddDependencyCommandDefinition.Name);
        var makeSealed = DaemonOwnedCommands.All
            .Single(command => command.Name == MakeSealedCommandDefinition.Name);

        addDependency.RequestType.ShouldBe(typeof(AddDependencyRequest));
        addDependency.ResponseType.ShouldBe(typeof(MutationExecutionResult<AddDependencyResult>));
        addDependency.Safety.ShouldBe(CommandSafety.ReviewByDefault);
        addDependency.SuggestedTools.Select(tool => tool.ToolName)
            .ShouldBe(["describe", "find_refs"]);

        makeSealed.RequestType.ShouldBe(typeof(MakeSealedRequest));
        makeSealed.ResponseType.ShouldBe(typeof(MutationExecutionResult<MakeSealedResult>));
        makeSealed.Safety.ShouldBe(CommandSafety.ReviewByDefault);
        makeSealed.SuggestedTools.Select(tool => tool.ToolName)
            .ShouldBe(["describe", "find_refs"]);
    }

    private sealed record EchoRequest(string Value);

    private sealed record EchoResponse(string Value);

    private sealed class EchoPort(string commandName = "echo")
        : CommandApplicationPort<EchoRequest, EchoResponse>
    {
        public int InvocationCount { get; private set; }

        public override ApplicationCommandDefinition Definition { get; } = CreateDefinition(commandName);

        public override Task<Result<EchoResponse, RtError>> ExecuteAsync(
            EchoRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(Result<EchoResponse, RtError>.Ok(
                new EchoResponse(request.Value.ToUpperInvariant())));
        }
    }

    private sealed class WorkingDirectoryPort
        : CommandApplicationPort<EchoRequest, EchoResponse>
    {
        public string? ObservedWorkingDirectory { get; private set; }

        public override ApplicationCommandDefinition Definition { get; } = CreateDefinition("working-directory");

        public override Task<Result<EchoResponse, RtError>> ExecuteAsync(
            EchoRequest request,
            CancellationToken cancellationToken = default)
        {
            ObservedWorkingDirectory = Directory.GetCurrentDirectory();
            return Task.FromResult(Result<EchoResponse, RtError>.Ok(
                new EchoResponse(ObservedWorkingDirectory)));
        }
    }

    private sealed class IoFailurePort
        : CommandApplicationPort<EchoRequest, EchoResponse>
    {
        public override ApplicationCommandDefinition Definition { get; } = CreateDefinition("io-failure");

        public override Task<Result<EchoResponse, RtError>> ExecuteAsync(
            EchoRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException("missing solution");
        }
    }

    private sealed class LegacyEchoPort(string commandName)
        : LegacyCommandApplicationPort<EchoRequest, EchoResponse>
    {
        public override string CommandName => commandName;

        public override Task<EchoResponse> ExecuteAsync(
            EchoRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EchoResponse(request.Value.ToUpperInvariant()));
    }

    private static ApplicationCommandDefinition CreateDefinition(string commandName) => new(
        new RtCommandInfo(
            commandName,
            false,
            true,
            "test",
            "Test command.",
            []),
        typeof(EchoRequest),
        typeof(EchoResponse),
        mcpVisible: false,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        commandName.Replace('-', '_') is "outline" or "add_dependency"
            ? [
                new CommandFollowOn("describe", "Inspect the result."),
                new CommandFollowOn("find_refs", "Find references.")
            ]
            : []);
}
