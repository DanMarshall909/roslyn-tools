using System.CommandLine;
using Microsoft.CodeAnalysis.Rename;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class RenameCommand : IRtCommand
{
    private RenameCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "rename",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["ren"],
        WhenToUse: "Renaming a C# symbol (type, method, property, field, namespace) and updating every reference across the solution. Prefer over grep+sed: rt uses Roslyn semantic rename so references that only bind by type inference are also updated, and string literals/comments are not accidentally rewritten.",
        Examples:
        [
            "rt rename OldTypeName NewTypeName --solution MySolution.sln",
            "rt rename MyNs.MyType.OldProp NewProp --kind property --dry-run",
            "rt ren FooService BarService              # short alias, auto-detect solution",
        ],
        KnownIssues:
        [
            "Invoking via `cmd //c` from bash on Windows crashes during workspace load — see ai-improvements row #13.",
        ]
    );

    public static Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified symbol name to rename.");
        var newNameArg = new Argument<string>("new-name", "New name for the symbol.");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file. Defaults to the single solution in current directory.");
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");

        var cmd = new Command("rename", "Rename a symbol and all its references across the solution")
        {
            symbolArg, newNameArg, solutionOpt, kindOpt, dryRunOpt
        };

        cmd.SetHandler(async (symbol, newName, solution, kind, dryRun) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);

                Console.Error.WriteLine($"Renaming '{sym.ToDisplayString()}' → '{newName}'...");

                var options = new SymbolRenameOptions(
                    RenameOverloads: false,
                    RenameInStrings: false,
                    RenameInComments: false,
                    RenameFile: false);

                var newSln = await Renamer.RenameSymbolAsync(sln, sym, options, newName);

                await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun);
            }
        },
        symbolArg, newNameArg, solutionOpt, kindOpt, dryRunOpt);

        cmd.AddAlias("ren");

        return cmd;
    }
}
