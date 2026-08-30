using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies duplicate symbols in the current file
/// </summary>
internal sealed class FindDuplicatesCommand(DaemonRpcRecoveryCoordinator daemon, FindDuplicatesOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly FindDuplicatesOperation _operation = operation;
    public static RtCommandInfo Info => FindDuplicatesCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var minStatementsOpt = new Option<int>("--min-statements", () => 3,
            "Minimum number of statements in a duplicate window");
        var minOccurrencesOpt =
            new Option<int>("--min-occurrences", () => 2, "Minimum number of occurrences to report a group");
        var scopeOpt = new Option<string?>("--scope", "Project name or namespace prefix to restrict search to");
        var includeTestsOpt =
            new Option<bool>("--include-tests", () => false, "Include test projects (excluded by default)");
        var maxStatementsOpt = new Option<int>("--max-statements", () => 40, "Hard cap on duplicate window size");

        var cmd = new Command("find-duplicates", "Report exact-clone code blocks repeated across the solution");
        cmd.AddAlias("fd");
        cmd.Add(solutionOpt);
        cmd.Add(minStatementsOpt);
        cmd.Add(minOccurrencesOpt);
        cmd.Add(scopeOpt);
        cmd.Add(includeTestsOpt);
        cmd.Add(maxStatementsOpt);

        cmd.SetHandler(async context =>
        {
            var request = new FindDuplicatesRequest(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(minStatementsOpt),
                context.ParseResult.GetValueForOption(minOccurrencesOpt),
                context.ParseResult.GetValueForOption(scopeOpt),
                context.ParseResult.GetValueForOption(includeTestsOpt),
                context.ParseResult.GetValueForOption(maxStatementsOpt));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                request,
                context.GetCancellationToken());
            result.Match(
                report => RtResultRenderer.Write(RtResult<FindDuplicatesResult>.Ok(
                    FindDuplicatesResult.FromGroups(
                        report.Groups,
                        report.SolutionDirectory,
                        report.SolutionPath))),
                error => RtErrorPresenter.Surface(context, error));
        });

        return cmd;
    }
}
