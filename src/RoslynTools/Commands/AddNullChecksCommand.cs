using System.CommandLine;

namespace RoslynTools.Commands;

internal static class AddNullChecksCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Name of the type containing the method") { IsRequired = true };
        var methodOpt = new Option<string>("--method", "Name of the method to add null checks to") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");

        var cmd = new Command("add-null-checks", "Insert ArgumentNullException.ThrowIfNull guards for reference-type parameters")
        {
            typeOpt, methodOpt, solutionOpt, dryRunOpt
        };
        cmd.AddAlias("anc");

        cmd.SetHandler(async (type, method, solution, dryRun) =>
        {
            await ExecuteAsync(type, method, solution, dryRun);
        },
        typeOpt, methodOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static Task ExecuteAsync(string typeName, string methodName, string? solutionPath, bool dryRun)
    {
        throw new NotImplementedException("add-null-checks is not yet implemented.");
    }
}
