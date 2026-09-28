using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class LanguageFeaturesHandlerTests
{
    [Fact]
    public void Descriptor_projects_the_typed_read_only_daemon_contract()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == LanguageFeaturesCommandDefinition.Name);
        var definition = DaemonOwnedCommands.All
            .Single(candidate => candidate.Name == LanguageFeaturesCommandDefinition.Name);

        descriptor.Port.ShouldNotBeNull();
        descriptor.Port.RequestType.ShouldBe(typeof(LanguageFeaturesRequest));
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.ReadOnly);
        definition.ResponseType.ShouldBe(typeof(LanguageFeaturesResult));
        definition.Safety.ShouldBe(CommandSafety.ReadOnly);

        var schema = provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(tool => tool.name == LanguageFeaturesCommandDefinition.Name)
            .inputSchema;
        var properties = schema["properties"]
            .ShouldBeOfType<Dictionary<string, Dictionary<string, object>>>();
        properties.Keys.ShouldContain("feature");
        properties.Keys.ShouldContain("includeLocations");
        properties.Keys.ShouldContain("refactorableOnly");
    }

    [Fact]
    public async Task Generic_adapter_returns_camel_case_feature_data_and_refactoring_candidates()
    {
        using var project = new AdhocProjectBuilder("LanguageFeaturesMcp");
        project.AddSource("Modern.cs", "namespace Demo; public record Value(int Id);");
        project.AddSource("Legacy.cs", "namespace Demo.Legacy { public class Value { } }");
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """
                   {
                     "feature": "file_scoped_namespace",
                     "includeLocations": true
                   }
                   """))
        {
            var adapter = new McpCommandAdapter(
                provider.GetRequiredService<CommandDescriptorRegistry>(),
                new DaemonRpcRecoveryCoordinator(new UnavailableRpcClient(), new UnavailableLauncher()));

            var result = await adapter.InvokeAsync(
                LanguageFeaturesCommandDefinition.Name,
                arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            var root = payload.RootElement;
            root.GetProperty("catalogVersion").GetString().ShouldBe("csharp-v1");
            var features = root.GetProperty("features").EnumerateArray().ToArray();
            features.Length.ShouldBe(1);
            var feature = features[0];
            feature.GetProperty("id").GetString().ShouldBe("file_scoped_namespace");
            feature.GetProperty("adoption").GetProperty("exceptions").GetInt32().ShouldBe(1);
            feature.GetProperty("refactoring").GetProperty("reviewCandidates").GetInt32().ShouldBe(1);
            feature.GetProperty("refactoring").GetProperty("candidateLocations")
                .GetArrayLength().ShouldBe(1);
        }
    }

    [Fact]
    public async Task Daemon_dispatcher_returns_the_same_typed_feature_contract()
    {
        using var project = new AdhocProjectBuilder("LanguageFeaturesDaemon");
        project.AddSource("Features.cs", "namespace Demo; public record Value(int Id);");
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """
                   {
                     "feature": "record_declaration"
                   }
                   """))
        {
            var response = await provider.GetRequiredService<DaemonCommandDispatcher>()
                .ExecuteAsync(new DaemonRpcRequest(
                    DaemonRpcProtocol.CurrentVersion,
                    "language-features-test",
                    DaemonRpcProtocol.ExecuteCommandMethod,
                    LanguageFeaturesCommandDefinition.Name,
                    arguments.RootElement.Clone()));

            response.Error.ShouldBeNull();
            var data = response.Result!.Value.GetProperty("data");
            data.GetProperty("catalogVersion").GetString().ShouldBe("csharp-v1");
            var features = data.GetProperty("features").EnumerateArray().ToArray();
            features.Length.ShouldBe(1);
            features[0].GetProperty("id").GetString().ShouldBe("record_declaration");
            features[0].GetProperty("occurrences").GetInt32().ShouldBe(1);
        }
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
