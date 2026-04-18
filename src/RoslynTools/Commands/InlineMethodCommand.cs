using System.CommandLine;

namespace RoslynTools.Commands;

internal static class InlineMethodCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class containing the method to inline") { IsRequired = true };
        var methodOpt = new Option<string>("--method", "Method name to inline at all call sites") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("inline-method", "Inline a method by replacing all call sites with the method body");
        cmd.AddAlias("im");
        cmd.Add(typeOpt); cmd.Add(methodOpt); cmd.Add(solutionOpt); cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, method, solution, dryRun) =>
        {
            await ExecuteAsync(type, method, solution, dryRun);
        }, typeOpt, methodOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static Task ExecuteAsync(string typeName, string methodName, string? solutionPath, bool dryRun)
    {
        throw new NotImplementedException("inline-method is not yet implemented.");
    }
}
