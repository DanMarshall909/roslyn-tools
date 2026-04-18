using System.CommandLine;

namespace RoslynTools.Commands;

internal static class MakeSealedCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to seal") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("make-sealed", "Add the sealed modifier to a class that has no derived types");
        cmd.AddAlias("ms");
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
        throw new NotImplementedException("make-sealed is not yet implemented.");
    }
}
