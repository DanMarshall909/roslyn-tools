using System.CommandLine;

namespace RoslynTools.Commands;

internal static class ConvertToRecordCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to convert to a record") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("convert-to-record", "Convert a class to a record type with positional parameters");
        cmd.AddAlias("ctr");
        cmd.Add(typeOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, solution, dryRun) =>
        {
            await ExecuteAsync(type, solution, dryRun);
        }, typeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static Task ExecuteAsync(string typeName, string? solutionPath, bool dryRun)
    {
        throw new NotImplementedException("convert-to-record is not yet implemented.");
    }
}
