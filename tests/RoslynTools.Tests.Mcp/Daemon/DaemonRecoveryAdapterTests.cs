using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Tools;
using RoslynTools.Models;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Daemon;

public sealed class DaemonRecoveryAdapterTests
{
    [Fact]
    public async Task Daemon_describe_applies_the_same_success_projection_as_local_execution()
    {
        using var provider = McpTestHost.CreateProvider();
        var response = new DescribeResult(
            DescribeResultKind.Compact,
            new CompactDescription("Example.Widget", "Class", ["void Run()"]),
            null);
        var adapter = CreateAdapter(provider, new CompletedRpcClient(response));
        using var arguments = JsonDocument.Parse("""{"symbol":"Example.Widget"}""");

        var result = await adapter.InvokeAsync("describe", arguments.RootElement);

        result.isError.ShouldBeFalse();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.GetProperty("fqn").GetString().ShouldBe("Example.Widget");
        payload.RootElement.TryGetProperty("data", out _).ShouldBeFalse();
        payload.RootElement.TryGetProperty("suggestedTools", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Daemon_single_rename_applies_the_same_success_projection_as_local_execution()
    {
        using var provider = McpTestHost.CreateProvider();
        var rename = new RenameResult(
            "Example.Widget.Run",
            "Execute",
            1,
            ["Widget.cs"],
            false,
            [],
            []);
        var response = MutationExecutionResult<RenameOperationResult>.Reviewed(
            new RenameOperationResult(RenameOperationMode.Single, [rename], 0));
        var adapter = CreateAdapter(provider, new CompletedRpcClient(response));
        using var arguments = JsonDocument.Parse(
            """{"symbol":"Example.Widget.Run","new-name":"Execute"}""");

        var result = await adapter.InvokeAsync("rename", arguments.RootElement);

        result.isError.ShouldBeFalse();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.GetProperty("symbol").GetString().ShouldBe("Example.Widget.Run");
        payload.RootElement.GetProperty("newName").GetString().ShouldBe("Execute");
        payload.RootElement.TryGetProperty("data", out _).ShouldBeFalse();
        payload.RootElement.TryGetProperty("status", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Indeterminate_mutation_is_not_retried_or_fallen_back_by_mcp()
    {
        using var provider = McpTestHost.CreateProvider();
        var rpc = new IndeterminateRpcClient();
        var launcher = new CountingLauncher();
        var adapter = CreateAdapter(provider, rpc, launcher);
        using var arguments = JsonDocument.Parse(
            """
            {
              "type": "AgentService",
              "dependency": "IEmailService",
              "dry-run": false
            }
            """);

        var result = await adapter.InvokeAsync(
            AddDependencyCommandDefinition.Name,
            arguments.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("indeterminate", Case.Insensitive);
        rpc.CallCount.ShouldBe(1);
        launcher.CallCount.ShouldBe(0);
    }

    private static McpCommandAdapter CreateAdapter(
        IServiceProvider provider,
        IDaemonRpcClient rpc,
        IDaemonLauncher? launcher = null) => new(
        provider.GetRequiredService<RoslynTools.CommandDescriptors.CommandDescriptorRegistry>(),
        provider.GetRequiredService<CliCommandBridge>(),
        new DaemonRpcRecoveryCoordinator(rpc, launcher ?? new CountingLauncher()));

    private sealed class CompletedRpcClient(object? data) : IDaemonRpcClient
    {
        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default)
        {
            var execution = new CommandExecutionResult(data, []);
            var response = new DaemonRpcResponse(
                DaemonRpcProtocol.CurrentVersion,
                "request",
                DaemonRpcJson.SerializeExecution(execution));
            return Task.FromResult(new DaemonRpcAttempt(
                DaemonRpcAttemptState.Completed,
                response,
                DispatchStarted: true));
        }
    }

    private sealed class IndeterminateRpcClient : IDaemonRpcClient
    {
        internal int CallCount { get; private set; }

        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new DaemonRpcAttempt(
                DaemonRpcAttemptState.IndeterminateAfterDispatch,
                null,
                DispatchStarted: true));
        }
    }

    private sealed class CountingLauncher : IDaemonLauncher
    {
        internal int CallCount { get; private set; }

        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(true);
        }
    }
}
