using System.CommandLine;

namespace RoslynTools.Commands;

internal static class SafeDeleteCommand
{
    internal static Command Build()
    {
        var symbolOpt = new Option<string>("--symbol", "Type or member FQN or simple name to delete") { IsRequired = true };
        var kindOpt = new Option<string?>("--kind", "Kind of symbol: type, method, or property");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("safe-delete", "Delete a type or member only if it has no references");
        cmd.AddAlias("sd");
        cmd.Add(symbolOpt);
        cmd.Add(kindOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (symbol, kind, solution, dryRun) =>
        {
            await ExecuteAsync(symbol, kind, solution, dryRun);
        }, symbolOpt, kindOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static Task ExecuteAsync(string symbol, string? kind, string? solutionPath, bool dryRun)
    {
        throw new NotImplementedException("safe-delete is not yet implemented.");
    }
}
