using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using RoslynTools.Tests;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class ExtractMethodHandlerTests
{
    [Fact]
    public void Descriptor_projects_the_typed_mutation_schema()
    {
        using var provider = CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>().All
            .Single(candidate => candidate.ToolName == ExtractMethodCommandDefinition.Definition.Name);

        descriptor.Port.ShouldBeOfType<ExtractMethodOperation>();
        descriptor.Definition.ShouldBeSameAs(ExtractMethodCommandDefinition.Definition);
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.DryRunByDefault);

        var schema = provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(tool => tool.name == ExtractMethodCommandDefinition.Definition.Name)
            .inputSchema;
        ((string[])schema["required"]).ShouldBe(
            ["file", "new-name", "start-line", "end-line"],
            ignoreOrder: true);
        var properties = (Dictionary<string, Dictionary<string, object>>)schema["properties"];
        properties["start-line"]["type"].ShouldBe("integer");
        properties.Keys.ShouldContain("dry-run");
        properties.Keys.ShouldContain("replace-duplicates");
    }

    [Fact]
    public async Task Generic_adapter_previews_success_and_returns_typed_range_and_not_found_failures()
    {
        using var project = new AdhocProjectBuilder("ExtractMethodMcp");
        project.AddSource("Calculator.cs", """
                                            namespace TestApp;

                                            public class Calculator
                                            {
                                                public void Process(int x, int y)
                                                {
                                                    var sum = x + y;
                                                    System.Console.WriteLine(sum);
                                                }
                                            }
                                            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = CreateProvider())
        using (var successArguments = JsonDocument.Parse($$"""
                                                        {
                                                          "file": "{{Path.Combine(project.ProjectDir, "Calculator.cs")}}",
                                                          "new-name": "Calculate",
                                                          "start-line": 7,
                                                          "end-line": 8
                                                        }
                                                        """))
        using (var missingArguments = JsonDocument.Parse($$"""
                                                        {
                                                          "file": "{{Path.Combine(project.ProjectDir, "Missing.cs")}}",
                                                          "new-name": "Calculate",
                                                          "start-line": 7,
                                                          "end-line": 8
                                                        }
                                                        """))
        using (var invalidRangeArguments = JsonDocument.Parse($$"""
                                                             {
                                                               "file": "{{Path.Combine(project.ProjectDir, "Calculator.cs")}}",
                                                               "new-name": "Calculate",
                                                               "start-line": 0,
                                                               "end-line": 8
                                                             }
                                                             """))
        {
            var adapter = new McpCommandAdapter(
                provider.GetRequiredService<CommandDescriptorRegistry>(),
                provider.GetRequiredService<CliCommandBridge>(),
                new DaemonRpcRecoveryCoordinator(
                    new UnavailableRpcClient(),
                    new UnavailableLauncher()));

            var success = await adapter.InvokeAsync(
                ExtractMethodCommandDefinition.Definition.Name,
                successArguments.RootElement);

            success.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(success.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            payload.RootElement.GetProperty("changedFiles").GetArrayLength().ShouldBe(1);

            var invalidRange = await adapter.InvokeAsync(
                ExtractMethodCommandDefinition.Definition.Name,
                invalidRangeArguments.RootElement);

            invalidRange.isError.ShouldBeTrue();
            using var rangeFailure = JsonDocument.Parse(invalidRange.content);
            rangeFailure.RootElement.GetProperty("kind").GetString().ShouldBe("InvalidInput");
            rangeFailure.RootElement.GetProperty("context").GetProperty("lineRange").GetString()
                .ShouldBe("0-8");

            var missing = await adapter.InvokeAsync(
                ExtractMethodCommandDefinition.Definition.Name,
                missingArguments.RootElement);

            missing.isError.ShouldBeTrue();
            using var failure = JsonDocument.Parse(missing.content);
            failure.RootElement.GetProperty("kind").GetString().ShouldBe("NotFound");
            failure.RootElement.GetProperty("context").GetProperty("file").GetString()
                .ShouldNotBeNull()
                .ShouldContain("Missing.cs", Case.Sensitive);
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());
        services.AddSingleton<IDaemonLauncher, UnavailableLauncher>();
        return services.BuildServiceProvider();
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
