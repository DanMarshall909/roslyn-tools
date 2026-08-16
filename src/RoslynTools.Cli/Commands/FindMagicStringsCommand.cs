using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies magic string literals in the current file
/// </summary>
internal sealed class FindMagicStringsCommand : IRtCommand, IAnalyzeCommand
{
    private readonly FindMagicStringsOperation _operation;

    public FindMagicStringsCommand(FindMagicStringsOperation operation)
    {
        _operation = operation;
    }

    public static RtCommandInfo Info => FindMagicStringsCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var minLengthOpt = new Option<int>("--min-length", () => 3, "Minimum string length to report");
        var minOccurrencesOpt = new Option<int>("--min-occurrences", () => 2, "Minimum occurrences of the same value");

        var cmd = new Command("find-magic-strings", "Report repeated string literals");
        cmd.AddAlias("magic-strings");
        cmd.AddAll(solutionOpt, minLengthOpt, minOccurrencesOpt);

        cmd.SetHandler(async (solution, minLength, minOccurrences) =>
        {
            var result = await _operation.ExecuteAsync(new FindMagicStringsRequest(
                minLength,
                minOccurrences,
                solution));
            RtResultRenderer.Write(RtResult<MagicStringResult>.Ok(
                MagicStringResult.FromGroups(result.Groups, result.SolutionPath)));
        }, solutionOpt, minLengthOpt, minOccurrencesOpt);

        return cmd;
    }
}
