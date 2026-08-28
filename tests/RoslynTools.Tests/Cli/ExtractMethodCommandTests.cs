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
    public async Task Binds_the_existing_cli_surface_to_the_typed_daemon_request()
    {
        var daemon = new RecordingDaemonRpcClient();
        var command = new ExtractMethodCommand(
            new DaemonRpcRecoveryCoordinator(daemon, new ThrowingDaemonLauncher()),
            new ExtractMethodOperation(
                new ThrowingWorkspaceSessionFactory(),
                new CapturingSolutionChangeWriter()));
        var root = new RootCommand();
        root.AddCommand(command.Build());

        var exitCode = await root.InvokeAsync(
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

        exitCode.ShouldBe(0);
        daemon.CommandName.ShouldBe(ExtractMethodCommandDefinition.Definition.Name);
        daemon.Safety.ShouldBe(CommandSafety.Mutation);
        daemon.Arguments.GetProperty("File").GetString().ShouldBe("src/Calculator.cs");
        daemon.Arguments.GetProperty("new-name").GetString().ShouldBe("Calculate");
        daemon.Arguments.GetProperty("start-line").GetInt32().ShouldBe(7);
        daemon.Arguments.GetProperty("end-line").GetInt32().ShouldBe(8);
        daemon.Arguments.GetProperty("After").GetString().ShouldBe("Process");
        daemon.Arguments.GetProperty("Solution").GetString().ShouldBe("App.slnx");
        daemon.Arguments.GetProperty("dry-run").GetBoolean().ShouldBeTrue();
        daemon.Arguments.GetProperty("replace-duplicates").GetBoolean().ShouldBeTrue();
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
