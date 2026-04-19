using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.VisualBasic.FileIO;
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
        WhenToUse: "Renaming a C# symbol (type, method, property, field, namespace) and updating every reference across the solution. Prefer over grep+sed: rt uses Roslyn semantic rename so references that only bind by type inference are also updated, and string literals/comments are not accidentally rewritten. Use --csv to batch-apply a curated rename mapping file.",
        Examples:
        [
            "rt rename OldTypeName NewTypeName --solution MySolution.sln",
            "rt rename MyNs.MyType.OldProp NewProp --kind property --dry-run",
            "rt rename --csv scripts/test-renames.csv --solution RoslynTools.slnx --dry-run",
            "rt ren FooService BarService              # short alias, auto-detect solution",
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
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file. Defaults to the single solution in current directory.");
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");
        var csvOpt = new Option<string?>("--csv", "Path to a CSV file with source_symbol and target_name columns for batch renames.");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");

        var cmd = new Command("rename", "Rename a symbol and all its references across the solution")
        {
            symbolArg, newNameArg, solutionOpt, kindOpt, csvOpt, dryRunOpt
        };

        cmd.SetHandler(async (symbol, newName, solution, kind, csv, dryRun) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                if (!string.IsNullOrWhiteSpace(csv))
                {
                    await ExecuteBatchAsync(sln, csv, dryRun);
                    return;
                }

                if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(newName))
                {
                    throw new InvalidOperationException("Provide either <symbol> <new-name> or --csv <path>.");
                }

                await ExecuteSingleAsync(sln, symbol, newName, kind, dryRun);
            }
        },
        symbolArg, newNameArg, solutionOpt, kindOpt, csvOpt, dryRunOpt);

        cmd.AddAlias("ren");

        return cmd;
    }

    internal static async Task<Solution> RenameAsync(
        Solution sln,
        string symbol,
        string newName,
        string? kind)
    {
        var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);

        Console.Error.WriteLine($"Renaming '{sym.ToDisplayString()}' -> '{newName}'...");

        var options = new SymbolRenameOptions(
            RenameOverloads: false,
            RenameInStrings: false,
            RenameInComments: false,
            RenameFile: false);

        return await Renamer.RenameSymbolAsync(sln, sym, options, newName);
    }

    internal static async Task ExecuteSingleAsync(
        Solution sln,
        string symbol,
        string newName,
        string? kind,
        bool dryRun)
    {
        var newSln = await RenameAsync(sln, symbol, newName, kind);
        await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun);
    }

    internal static async Task ExecuteBatchAsync(
        Solution sln,
        string csvPath,
        bool dryRun)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("CSV mapping file was not found.", csvPath);
        }

        var rows = ReadBatchRows(csvPath);
        var flagged = rows.Where(r => !string.IsNullOrWhiteSpace(r.Review)).ToList();
        if (flagged.Count > 0)
        {
            throw new InvalidOperationException(
                $"CSV contains {flagged.Count} row(s) with a non-empty review column. Curate first.");
        }

        var currentSln = sln;
        foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.NewName)))
        {
            if (string.IsNullOrWhiteSpace(row.OldFqn))
            {
                continue;
            }

            var newSln = await RenameAsync(currentSln, row.OldFqn, row.NewName!, kind: null);
            await SolutionWriter.ApplyChangesAsync(currentSln, newSln, dryRun);
            currentSln = newSln;
        }
    }

    private static List<RenameCsvRow> ReadBatchRows(string csvPath)
    {
        using var parser = new TextFieldParser(csvPath);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        if (parser.EndOfData)
        {
            throw new InvalidOperationException("CSV mapping file is empty.");
        }

        var headers = parser.ReadFields() ?? throw new InvalidOperationException("CSV header row is missing.");
        var map = headers
            .Select((header, index) => (header, index))
            .ToDictionary(x => x.header, x => x.index, StringComparer.OrdinalIgnoreCase);

        if (!map.TryGetValue("source_symbol", out var oldFqnIndex))
        {
            throw new InvalidOperationException("CSV must contain a source_symbol column.");
        }

        if (!map.TryGetValue("target_name", out var newNameIndex))
        {
            throw new InvalidOperationException("CSV must contain a target_name column.");
        }

        var hasReviewColumn = map.TryGetValue("review", out var reviewIndex);

        var rows = new List<RenameCsvRow>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null)
            {
                continue;
            }

            rows.Add(new RenameCsvRow(
                GetField(fields, oldFqnIndex),
                GetField(fields, newNameIndex),
                hasReviewColumn ? GetField(fields, reviewIndex) : null));
        }

        return rows;
    }

    private static string? GetField(string[] fields, int index)
        => index >= 0 && index < fields.Length ? fields[index] : null;

    private sealed record RenameCsvRow(string? OldFqn, string? NewName, string? Review);
}
