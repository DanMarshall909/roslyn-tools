using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class ArchitectureCheckCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ArchitectureCheckOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ArchitectureCheckOperation _operation = operation;

    public static RtCommandInfo Info => ArchitectureCheckCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var configOpt = new Option<string>(
            "--config",
            () => ".rt/architecture.json",
            "Checked-in architecture JSON path relative to the solution directory");
        var baselineOpt = new Option<string?>(
            "--baseline",
            "Optional baseline JSON path; violations remain visible and are labelled existing or new");
        var command = new Command(
            "check-architecture",
            "Validate configured project and source dependency boundaries");
        command.AddAlias("arch-check");
        command.AddAll(solutionOpt, configOpt, baselineOpt);

        command.SetHandler(async context =>
        {
            var request = new ArchitectureCheckRequest(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(configOpt)!,
                context.ParseResult.GetValueForOption(baselineOpt));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                request,
                context.GetCancellationToken());
            result.Match(
                value =>
                {
                    RtResultRenderer.Write(RtResult<ArchitectureCheckResult>.Ok(value));
                    if (!value.IsCompliant)
                        context.ExitCode = 1;
                },
                error => RtErrorPresenter.Surface(context, error));
        });

        return command;
    }
}
