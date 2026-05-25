using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Renames symbols within the solution.
/// </summary>
internal sealed class RenameCommand : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly RenameService _rename;
    public RenameCommand(IWorkspaceSessionFactory sessionFactory, RenameService rename)
    {
        _rename = rename;
        _sessionFactory = sessionFactory;
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

    public Command Build()
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
                var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
                using (workspace)
                {
                    if (!string.IsNullOrWhiteSpace(csv))
                    {
                        await _rename.ExecuteBatchAsync(sln, csv, dryRun);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(newName))
                        throw new InvalidOperationException("Provide either <symbol> <new-name> or --csv <path>.");

                    await _rename.ExecuteSingleAsync(sln, symbol, newName, kind, dryRun);
                }
            },
            symbolArg, newNameArg, solutionOpt, kindOpt, csvOpt, dryRunOpt);

        cmd.AddAlias("ren");

        return cmd;
    }

    /// <summary>
    /// Asynchronously renames multiple symbols in the solution.
    /// </summary>
    internal Task ExecuteBatchAsync(
        Solution sln, string csvPath, bool dryRun)
    {
        return _rename.ExecuteBatchAsync(sln, csvPath, dryRun);
    }
}
