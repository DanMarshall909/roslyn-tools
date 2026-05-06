using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.VisualBasic.FileIO;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class RenameService
{
    internal async Task<Solution> RenameAsync(
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

    internal async Task ExecuteSingleAsync(
        Solution sln,
        string symbol,
        string newName,
        string? kind,
        bool dryRun)
    {
        await RenameOneAsync(sln, symbol, newName, kind, dryRun);
    }

    private async Task<Solution> RenameOneAsync(
        Solution sln,
        string symbol,
        string newName,
        string? kind,
        bool dryRun)
    {
        var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);
        var fileRename = ComputeFileRename(sym, newName);

        RtLog.Info("Renaming '{Symbol}' -> '{NewName}'...", sym.ToDisplayString(), newName);

        var newSln = await Renamer.RenameSymbolAsync(
            sln,
            sym,
            new SymbolRenameOptions(RenameOverloads: false, RenameInStrings: false, RenameInComments: false, RenameFile: false),
            newName);

        await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun);

        if (fileRename is { } rename)
            ApplyFileRename(rename, dryRun);

        return newSln;
    }

    private (string OldPath, string NewPath)? ComputeFileRename(ISymbol sym, string newName)
    {
        if (sym is not INamedTypeSymbol) return null;

        var syntaxRef = sym.DeclaringSyntaxReferences.FirstOrDefault();
        var filePath = syntaxRef?.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath)) return null;

        var basename = Path.GetFileNameWithoutExtension(filePath);
        if (!string.Equals(basename, sym.Name, StringComparison.Ordinal)) return null;

        var dir = Path.GetDirectoryName(filePath)!;
        var ext = Path.GetExtension(filePath);
        return (filePath, Path.Combine(dir, newName + ext));
    }

    private void ApplyFileRename((string OldPath, string NewPath) rename, bool dryRun)
    {
        if (dryRun)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"[dry-run] Would rename: {rename.OldPath} -> {rename.NewPath}");
            return;
        }

        if (File.Exists(rename.NewPath))
        {
            RtLog.Warning("Target file already exists: {Path} — file not renamed.", rename.NewPath);
            return;
        }

        File.Move(rename.OldPath, rename.NewPath);
        RtConsole.WriteLine(RtOutputContext.Result, $"Renamed file: {rename.OldPath} -> {rename.NewPath}");
    }

    internal async Task ExecuteBatchAsync(
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

            currentSln = await RenameOneAsync(currentSln, row.OldFqn, row.NewName!, kind: null, dryRun);
        }
    }

    internal List<RenameCsvRow> ReadBatchRows(string csvPath)
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

    internal string? GetField(string[] fields, int index)
        => index >= 0 && index < fields.Length ? fields[index] : null;

    internal sealed record RenameCsvRow(string? OldFqn, string? NewName, string? Review);
}
