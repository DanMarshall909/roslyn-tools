using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public sealed class ExtractMethodCommandTests
{
    [Fact]
    public void Publishes_a_review_first_in_process_cli_surface()
    {
        var daemon = new RecordingDaemonRpcClient();
        var command = new ExtractMethodCommand(
            new DaemonRpcRecoveryCoordinator(daemon, new ThrowingDaemonLauncher()),
            new ExtractMethodOperation(
                new ThrowingWorkspaceSessionFactory(),
                new CapturingSolutionChangeWriter()));
        var built = command.Build();
        var root = new RootCommand();
        root.AddCommand(built);

        var parse = root.Parse(
            [
                "extract-method",
                "src/Calculator.cs",
                "Calculate",
                "--start-line", "7",
                "--end-line", "8",
                "--after", "Process",
                "--solution", "App.slnx",
                "--dry-run",
                "--replace-duplicates"
            ]);

        parse.Errors.ShouldBeEmpty();
        var dryRun = built.Options.OfType<Option<bool>>()
            .Single(option => option.Name == "dry-run");
        parse.GetValueForOption(dryRun).ShouldBeTrue();

        var definition = ExtractMethodCommandDefinition.Definition;
        definition.McpVisible.ShouldBeFalse();
        definition.Safety.ShouldBe(CommandSafety.ReviewByDefault);
        definition.ExecutionPlacement.ShouldBe(CommandExecutionPlacement.InProcess);
        daemon.CommandName.ShouldBeNull();
    }

    private sealed class RecordingDaemonRpcClient : IDaemonRpcClient
    {
        private static readonly JsonElement Success = JsonDocument.Parse("""{"data":{}}""").RootElement.Clone();

        public string? CommandName { get; private set; }
        public CommandSafety? Safety { get; private set; }
        public JsonElement Arguments { get; private set; }

        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default)
        {
            CommandName = commandName;
            Safety = safety;
            Arguments = arguments.Clone();
            return Task.FromResult(new DaemonRpcAttempt(
                DaemonRpcAttemptState.Completed,
                new DaemonRpcResponse(
                    DaemonRpcProtocol.CurrentVersion,
                    "extract-method-test",
                    Success),
                DispatchStarted: true));
        }
    }

    private sealed class ThrowingDaemonLauncher : IDaemonLauncher
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A completed daemon request must not start a daemon.");
    }

    private sealed class ThrowingWorkspaceSessionFactory : IWorkspaceSessionFactory
    {
        public Task<Microsoft.CodeAnalysis.Solution> GetSolutionAsync(string? solutionPath) =>
            throw new InvalidOperationException("The daemon path must not load a local workspace.");

        public Task<(Microsoft.CodeAnalysis.Workspace Workspace, Microsoft.CodeAnalysis.Solution Solution)>
            LoadWorkspaceAsync(string? solutionPath) =>
            throw new InvalidOperationException("The daemon path must not load a local workspace.");
    }
}
