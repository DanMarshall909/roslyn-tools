using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Renames symbols within the solution.
/// </summary>
internal sealed class RenameCommand : IRtCommand
{
    private RenameCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "rename",
        true,
        false,
        "refactor",
        Aliases: ["ren"],
        WhenToUse:
        "Renaming a C# symbol (type, method, property, field, namespace) and updating every reference across the solution. Prefer over grep+sed: rt uses Roslyn semantic rename so references that only bind by type inference are also updated, and string literals/comments are not accidentally rewritten. Use --csv to batch-apply a curated rename mapping file.",
        Examples:
        [
            "rt refactor rename OldTypeName NewTypeName --solution MySolution.sln",
            "rt refactor rename MyNs.MyType.OldProp NewProp --kind property --dry-run",
            "rt refactor rename --csv scripts/renames.csv --solution MySolution.sln --dry-run",
            "rt refactor ren FooService BarService              # short alias, auto-detect solution"
        ]
    );

    public static Command Build()
    {
        var symbolArg = new Argument<string?>("symbol", () => null, "Fully-qualified symbol name to rename.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var newNameArg = new Argument<string?>("new-name", () => null, "New name for the symbol.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file. Defaults to the single solution in current directory.");
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");
        var csvOpt = new Option<string?>("--csv",
            "Path to a CSV file with source_symbol/target_name, or prefixed *_fqn plus *_name_new columns.");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");

        var cmd = new Command("rename", "Rename a symbol and all its references across the solution")
        {
            symbolArg, newNameArg, solutionOpt, kindOpt, csvOpt, dryRunOpt
        };

        cmd.SetHandler(async (symbol, newName, solution, kind, csv, dryRun) =>
            {
                var (workspace, sln) = await RtServiceProvider.GetOrDefault(IWorkspaceSessionFactory.CreateDefault).LoadWorkspaceAsync(solution);
                using (workspace)
                {
                    if (!string.IsNullOrWhiteSpace(csv))
                    {
                        await RtServiceProvider.Get<RenameService>().ExecuteBatchAsync(sln, csv, dryRun);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(newName))
                        throw new InvalidOperationException("Provide either <symbol> <new-name> or --csv <path>.");

                    await RtServiceProvider.Get<RenameService>().ExecuteSingleAsync(sln, symbol, newName, kind, dryRun);
                }
            },
            symbolArg, newNameArg, solutionOpt, kindOpt, csvOpt, dryRunOpt);

        cmd.AddAlias("ren");

        return cmd;
    }

    // Forwarding stubs kept for backwards compatibility
    /// <summary>
    /// Asynchronously renames symbols in the solution with additional parameters.
    /// </summary>
    internal static Task<Solution> RenameAsync(
        Solution sln, string symbol, string newName, string? kind)
    {
        return RtServiceProvider.Get<RenameService>().RenameAsync(sln, symbol, newName, kind);
    }

    /// <summary>
    /// Asynchronously renames a single symbol in the solution.
    /// </summary>
    internal static Task ExecuteSingleAsync(
        Solution sln, string symbol, string newName, string? kind, bool dryRun)
    {
        return RtServiceProvider.Get<RenameService>().ExecuteSingleAsync(sln, symbol, newName, kind, dryRun);
    }

    /// <summary>
    /// Asynchronously renames multiple symbols in the solution.
    /// </summary>
    internal static Task ExecuteBatchAsync(
        Solution sln, string csvPath, bool dryRun)
    {
        return RtServiceProvider.Get<RenameService>().ExecuteBatchAsync(sln, csvPath, dryRun);
    }
}
