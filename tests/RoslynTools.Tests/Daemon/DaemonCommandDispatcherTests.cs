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
    public async Task Execute_adds_core_owned_follow_on_tools_before_any_adapter_renders()
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

        public override string CommandName => commandName;

        public override Task<EchoResponse> ExecuteAsync(
            EchoRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(new EchoResponse(request.Value.ToUpperInvariant()));
        }
    }
}
