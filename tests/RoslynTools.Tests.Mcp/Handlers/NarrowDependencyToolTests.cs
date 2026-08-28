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

[Collection("mcp-bridge")]
public sealed class NarrowDependencyToolTests
{
    [Fact]
    public async Task generic_mcp_defaults_to_dry_run_and_returns_the_structured_narrowing_report()
    {
        using var project = BuildProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"interface":"IWideFacade","members":["Send"],"new-name":"ISender","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider).InvokeAsync("narrow_dependency", arguments.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            var value = payload.RootElement.GetProperty("value");
            value.GetProperty("interfaceCreated").GetBoolean().ShouldBeTrue();
            value.GetProperty("narrowedConsumers").GetArrayLength().ShouldBe(1);
            value.GetProperty("skippedConsumers").EnumerateArray().First().GetProperty("reason").GetString()!
                .ShouldContain("outside the selected subset", Case.Insensitive);
            value.GetProperty("diRegistrationFollowUp").GetString()!
                .ShouldContain("DI registration", Case.Insensitive);
            project.SourceExists("ISender.cs").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task generic_mcp_returns_a_typed_invalid_member_error_without_writing()
    {
        using var project = BuildProject();
        var (workspace, solution) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """{"interface":"IWideFacade","members":["Missing"],"new-name":"ISender","solution":"ignored.slnx"}"""))
        {
            var result = await CreateAdapter(provider).InvokeAsync("narrow_dependency", arguments.RootElement);

            result.isError.ShouldBeTrue();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
            payload.RootElement.GetProperty("message").GetString()!.ShouldContain("Missing", Case.Sensitive);
            project.SourceExists("ISender.cs").ShouldBeFalse();
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());
        services.AddSingleton<IDaemonLauncher, UnavailableDaemonLauncher>();
        return services.BuildServiceProvider();
    }

    private static McpCommandAdapter CreateAdapter(IServiceProvider provider) => new(
        provider.GetRequiredService<CommandDescriptorRegistry>(),
        provider.GetRequiredService<CliCommandBridge>(),
        new DaemonRpcRecoveryCoordinator(new UnavailableRpcClient(), new UnavailableDaemonLauncher()));

    private static AdhocProjectBuilder BuildProject()
    {
        var project = new AdhocProjectBuilder("WideFacadeApp");
        project.AddSource("IWideFacade.cs", """
                                            namespace MyApp;
                                            public interface IWideFacade { void Send(); void Receive(); }
                                            """);
        project.AddSource("Consumer.cs", """
                                         namespace MyApp;
                                         public sealed class SenderOnlyConsumer
                                         {
                                             private readonly IWideFacade _facade;
                                             public SenderOnlyConsumer(IWideFacade facade) => _facade = facade;
                                             public void Run() => _facade.Send();
                                         }
                                         public sealed class MixedConsumer
                                         {
                                             private readonly IWideFacade _facade;
                                             public MixedConsumer(IWideFacade facade) => _facade = facade;
                                             public void Run() { _facade.Send(); _facade.Receive(); }
                                         }
                                         """);
        return project;
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

    private sealed class UnavailableDaemonLauncher : IDaemonLauncher
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
