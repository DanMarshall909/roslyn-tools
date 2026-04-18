using System.CommandLine;

namespace RoslynTools.Commands;

internal static class ReplaceCallCommand
{
    internal static Command Build()
    {
        var fromOpt = new Option<string>("--from", "Full method reference to replace, e.g. Logger.Log") { IsRequired = true };
        var toOpt = new Option<string>("--to", "New method name, e.g. LogInfo") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");

        var cmd = new Command("replace-call", "Replace all invocations of one static method with another")
        {
            fromOpt, toOpt, solutionOpt, dryRunOpt
        };
        cmd.AddAlias("rc");

        cmd.SetHandler(async (from, to, solution, dryRun) =>
        {
            await ExecuteAsync(from, to, solution, dryRun);
        },
        fromOpt, toOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static Task ExecuteAsync(string from, string to, string? solutionPath, bool dryRun)
    {
        throw new NotImplementedException("replace-call is not yet implemented.");
    }
}
