using System.CommandLine;
using Microsoft.CodeAnalysis.Rename;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class RenameCommand
{
    internal static Command Build()
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
