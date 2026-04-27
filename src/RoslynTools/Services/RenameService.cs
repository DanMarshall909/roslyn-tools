using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.VisualBasic.FileIO;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class RenameService
{
    internal static async Task<Solution> RenameAsync(
        Solution sln,
        string symbol,
        string newName,
        string? kind)
    {
        var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);

        RtLog.Info("Renaming '{Symbol}' -> '{NewName}'...", sym.ToDisplayString(), newName);

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
            throw new FileNotFoundException("CSV mapping file was not found.", csvPath);

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
                continue;

            var newSln = await RenameAsync(currentSln, row.OldFqn, row.NewName!, kind: null);
            await SolutionWriter.ApplyChangesAsync(currentSln, newSln, dryRun);
            currentSln = newSln;
        }
    }

    internal static List<RenameCsvRow> ReadBatchRows(string csvPath)
    {
        using var parser = new TextFieldParser(csvPath);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        if (parser.EndOfData)
            throw new InvalidOperationException("CSV mapping file is empty.");

        var headers = parser.ReadFields() ?? throw new InvalidOperationException("CSV header row is missing.");
        var map = headers
            .Select((header, index) => (header, index))
            .ToDictionary(x => x.header, x => x.index, StringComparer.OrdinalIgnoreCase);

        if (!map.TryGetValue("source_symbol", out var oldFqnIndex))
            throw new InvalidOperationException("CSV must contain a source_symbol column.");

        if (!map.TryGetValue("target_name", out var newNameIndex))
            throw new InvalidOperationException("CSV must contain a target_name column.");

        var hasReviewColumn = map.TryGetValue("review", out var reviewIndex);

        var rows = new List<RenameCsvRow>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null)
                continue;

            rows.Add(new RenameCsvRow(
                GetField(fields, oldFqnIndex),
                GetField(fields, newNameIndex),
                hasReviewColumn ? GetField(fields, reviewIndex) : null));
        }

        return rows;
    }

    internal static string? GetField(string[] fields, int index)
        => index >= 0 && index < fields.Length ? fields[index] : null;

    internal sealed record RenameCsvRow(string? OldFqn, string? NewName, string? Review);
}
