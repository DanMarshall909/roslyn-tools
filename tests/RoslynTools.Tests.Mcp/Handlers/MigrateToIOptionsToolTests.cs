using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using RoslynTools.Tests;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public sealed class MigrateToIOptionsToolTests
{
    [Fact]
    public void descriptor_projects_the_typed_schema()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == "migrate_to_ioptions");
        var tool = ToolSchemaBuilder.Build(descriptor);

        descriptor.Port.ShouldBeOfType<MigrateToIOptionsOperation>();
        descriptor.Definition.ShouldBeSameAs(MigrateToIOptionsCommandDefinition.Definition);
        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];
        ((string[])tool.inputSchema["required"]).ShouldBe(["section", "name"], ignoreOrder: true);
        properties.Keys.ShouldBe(["section", "name", "solution", "dry-run"], ignoreOrder: true);
    }

    [Fact]
    public async Task generic_mcp_defaults_to_preview_and_returns_structured_success()
    {
        using var project = BuildProject();
        var (workspace, solution) = project.Build();
        File.WriteAllText(Path.Combine(project.ProjectDir, "appsettings.json"), """
            { "MyFeature": { "Enabled": true } }
            """);

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"section":"MyFeature","name":"MyFeatureOptions","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider).InvokeAsync("migrate_to_ioptions", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            payload.RootElement.GetProperty("value").GetProperty("generatedType").GetString()
                .ShouldBe("MyFeatureOptions");
            payload.RootElement.GetProperty("value").GetProperty("discoveredConfigSources").GetArrayLength()
                .ShouldBe(1);
            File.Exists(Path.Combine(project.ProjectDir, "MyFeatureOptions.cs")).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task generic_mcp_returns_a_typed_validation_failure()
    {
        using var provider = McpTestHost.CreateProvider();
        using var arguments = JsonDocument.Parse("""{"section":"MyFeature","name":"myFeatureOptions"}""");

        var result = await CreateAdapter(provider).InvokeAsync("migrate_to_ioptions", arguments.RootElement);

        result.isError.ShouldBeTrue();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
        payload.RootElement.GetProperty("message").GetString()!.ShouldContain("PascalCase", Case.Sensitive);
    }

    private static AdhocProjectBuilder BuildProject()
    {
        var project = new AdhocProjectBuilder("OptionsApp");
        project.AddSource("Startup.cs", """
            namespace OptionsApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddLogging();
                }
            }
            """);
        project.AddSource("Reader.cs", """
            namespace OptionsApp;

            public sealed class Reader
            {
                public string Read(IConfiguration configuration) =>
                    configuration.GetValue<string>("MyFeature:Endpoint");
            }
            """);
        return project;
    }

    private static McpCommandAdapter CreateAdapter(IServiceProvider provider) => new(
        provider.GetRequiredService<CommandDescriptorRegistry>(),
        provider.GetRequiredService<CliCommandBridge>(),
        new DaemonRpcRecoveryCoordinator(new UnavailableRpcClient(), new UnavailableLauncher()));

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
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
