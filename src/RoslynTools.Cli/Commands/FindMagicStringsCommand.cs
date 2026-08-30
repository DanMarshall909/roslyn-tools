using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Cli;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies magic string literals in the current file
/// </summary>
internal sealed class FindMagicStringsCommand(DaemonRpcRecoveryCoordinator daemon, FindMagicStringsOperation operation) : IRtCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly FindMagicStringsOperation _operation = operation;

    public static RtCommandInfo Info => FindMagicStringsCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var minLengthOpt = new Option<int>("--min-length", () => 3, "Minimum string length to report");
        var minOccurrencesOpt = new Option<int>("--min-occurrences", () => 2, "Minimum occurrences of the same value");

        var cmd = new Command("find-magic-strings", "Report repeated string literals");
        cmd.AddAlias("magic-strings");
        cmd.AddAll(solutionOpt, minLengthOpt, minOccurrencesOpt);

        cmd.SetHandler(async context =>
        {
            var request = new FindMagicStringsRequest(
                context.ParseResult.GetValueForOption(minLengthOpt),
                context.ParseResult.GetValueForOption(minOccurrencesOpt),
                context.ParseResult.GetValueForOption(solutionOpt));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon, _operation, request, context.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<FindMagicStringsResult>.Ok(value)),
                error => RtErrorPresenter.Surface(context, error));
        });

        return cmd;
    }
}
