using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class FindPatternHandlerTests
{
    private static readonly string[] RequiredArguments = ["file", "startLine", "endLine"];

    [Fact]
    public void Descriptor_projects_the_typed_read_only_daemon_contract()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == FindPatternCommandDefinition.Name);
        var definition = DaemonOwnedCommands.All
            .Single(candidate => candidate.Name == FindPatternCommandDefinition.Name);

        descriptor.Port.ShouldNotBeNull();
        descriptor.Port.RequestType.ShouldBe(typeof(FindPatternRequest));
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.ReadOnly);
        definition.ResponseType.ShouldBe(typeof(FindPatternResult));
        definition.Safety.ShouldBe(CommandSafety.ReadOnly);

        var schema = provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(tool => tool.name == FindPatternCommandDefinition.Name)
            .inputSchema;
        schema["required"].ShouldBe(RequiredArguments);
        var properties = schema["properties"]
            .ShouldBeOfType<Dictionary<string, Dictionary<string, object>>>();
        properties["startLine"]["type"].ShouldBe("integer");
        properties.Keys.ShouldContain("scope");
    }

    [Fact]
    public async Task Generic_adapter_returns_structured_matches_without_cli_capture()
    {
        using var project = new AdhocProjectBuilder("PatternMcp");
        project.AddSource("Source.cs", """
            namespace PatternMcp;
            public class Source
            {
                public void Run()
                {
                    int first = 1;
                    System.Console.WriteLine(first);
                }
            }
            """);
        project.AddSource("Match.cs", """
            namespace PatternMcp;
            public class Match
            {
                public void Run()
                {
                    int second = 2;
                    System.Console.WriteLine(second);
                }
            }
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse($$"""
                   {
                     "file": {{JsonSerializer.Serialize(Path.Combine(project.ProjectDir, "Source.cs"))}},
                     "startLine": 6,
                     "endLine": 7
                   }
                   """))
        {
            var adapter = new McpCommandAdapter(
                provider.GetRequiredService<CommandDescriptorRegistry>(),
                new DaemonRpcRecoveryCoordinator(new UnavailableRpcClient(), new UnavailableLauncher()));

            var result = await adapter.InvokeAsync(
                FindPatternCommandDefinition.Name,
                arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            var match = payload.RootElement.GetProperty("matches")
                .EnumerateArray().ShouldHaveSingleItem();
            match.GetProperty("file").GetString().ShouldEndWith("Match.cs");
        }
    }

    [Fact]
    public async Task Daemon_dispatcher_returns_typed_errors_for_invalid_ranges()
    {
        using var provider = McpTestHost.CreateProvider();
        using var arguments = JsonDocument.Parse(
            """
            {
              "file": "Source.cs",
              "startLine": 0,
              "endLine": 1
            }
            """);

        var response = await provider.GetRequiredService<DaemonCommandDispatcher>()
            .ExecuteAsync(new DaemonRpcRequest(
                DaemonRpcProtocol.CurrentVersion,
                "find-pattern-test",
                DaemonRpcProtocol.ExecuteCommandMethod,
                FindPatternCommandDefinition.Name,
                arguments.RootElement.Clone()));

        response.Result.ShouldBeNull();
        response.Error.ShouldNotBeNull();
        response.Error.Code.ShouldBe(DaemonRpcErrorCodes.CommandFailed);
        response.Error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        response.Error.Message.ShouldContain("line range");
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
