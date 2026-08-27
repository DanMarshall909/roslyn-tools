using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies magic string literals in the current file
/// </summary>
internal sealed class FindMagicStringsCommand(DaemonRpcClient daemon) : IRtCommand, IAnalyzeCommand
{
    private readonly DaemonRpcClient _daemon = daemon;

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
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                FindMagicStringsCommandDefinition.Name,
                request,
                context);
        });

        return cmd;
    }
}
