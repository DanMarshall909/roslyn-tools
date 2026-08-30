using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for analyzing an interface for issues.
/// </summary>
internal sealed class AnalyzeInterfaceCommand(DaemonRpcRecoveryCoordinator daemon, AnalyzeInterfaceOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly AnalyzeInterfaceOperation _operation = operation;
    public static RtCommandInfo Info => AnalyzeInterfaceCommandDefinition.Info;

    public Command Build()
    {
        var interfaceArg = new Argument<string>("interface", "Interface symbol to analyze");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var scopeOpt = new Option<string?>("--scope", "Project name or namespace prefix to restrict analysis");
        var includeTestsOpt = new Option<bool>("--include-tests", () => false, "Include test projects in results");
        var suggestOpt = new Option<bool>("--suggest", () => false,
            "Augment output with advisory minimal replacement interface suggestions per consumer");

        var cmd = new Command("analyze-interface", "Group interface consumers by the exact members they use");
        cmd.Add(interfaceArg);
        cmd.Add(solutionOpt);
        cmd.Add(scopeOpt);
        cmd.Add(includeTestsOpt);
        cmd.Add(suggestOpt);

        cmd.SetHandler(async context =>
        {
            var request = new AnalyzeInterfaceRequest(
                context.ParseResult.GetValueForArgument(interfaceArg),
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(scopeOpt),
                context.ParseResult.GetValueForOption(includeTestsOpt),
                context.ParseResult.GetValueForOption(suggestOpt));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                request,
                context.GetCancellationToken());
            result.Match(
                report => RtResultRenderer.Write(RtResult<AnalyzeInterfaceResult>.Ok(
                    AnalyzeInterfaceResult.FromReport(report, request.Suggest))),
                error => RtErrorPresenter.Surface(context, error));
        });

        return cmd;
    }
}
