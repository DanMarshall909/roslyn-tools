using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class RenameService : IRtService
{
    private readonly ILogger<RenameService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;
    private readonly ISymbolResolutionService _symbols;

    public RenameService()
        : this(new RtConsoleLogger<RenameService>(), new SolutionChangeWriter())
    {
    }

    public RenameService(ILogger<RenameService> logger, ISolutionChangeWriter solutionChangeWriter,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();

        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    internal async Task<Solution> RenameAsync(
        Solution sln,
        string symbol,
        string newName,
        string? kind)
    {
        var sym = await _symbols.FindSymbolByFqnAsync(sln, symbol, kind);

        _logger.RtInfo("Renaming '{Symbol}' -> '{NewName}'...", sym.ToDisplayString(), newName);

        var options = new SymbolRenameOptions(
            false,
            false,
            false,
            false);

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
        var sym = await _symbols.FindSymbolByFqnAsync(sln, symbol, kind);
        var fileRename = ComputeFileRename(sym, newName);

        _logger.RtInfo("Renaming '{Symbol}' -> '{NewName}'...", sym.ToDisplayString(), newName);

        var newSln = await Renamer.RenameSymbolAsync(
            sln,
            sym,
            new SymbolRenameOptions(false, false, false, false),
            newName);

        await _solutionChangeWriter.ApplyChangesAsync(sln, newSln, dryRun);

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
            RtConsole.WriteLine(RtOutputContext.Result,
                $"[dry-run] Would rename: {rename.OldPath} -> {rename.NewPath}");
            return;
        }

        if (File.Exists(rename.NewPath))
        {
            _logger.RtWarning("Target file already exists: {Path} — file not renamed.", rename.NewPath);
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
            throw new InvalidOperationException(
                $"CSV contains {flagged.Count} row(s) with a non-empty review column. Curate first.");

        var currentSln = sln;
        foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.NewName)))
        {
            if (string.IsNullOrWhiteSpace(row.OldFqn))
                continue;

            currentSln = await RenameOneOrSkipAlreadyAppliedAsync(currentSln, row, dryRun);
        }
    }

    private async Task<Solution> RenameOneOrSkipAlreadyAppliedAsync(
        Solution sln,
        RenameCsvRow row,
        bool dryRun)
    {
        try
        {
            return await RenameOneAsync(sln, row.OldFqn!, row.NewName!, null, dryRun);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                                   $"No symbol with FQN '{row.OldFqn}' found in solution.",
                                                   StringComparison.Ordinal))
        {
            if (await AnyAlreadyAppliedTargetExistsAsync(sln, row))
            {
                _logger.RtInfo(
                    "Skipping already-applied rename '{Symbol}' -> '{NewName}'.",
                    row.OldFqn,
                    row.NewName);
                return sln;
            }

            throw;
        }
    }

    private async Task<bool> AnyAlreadyAppliedTargetExistsAsync(Solution sln, RenameCsvRow row)
    {
        foreach (var targetFqn in GetAlreadyAppliedTargetFqns(row))
        {
            try
            {
                await _symbols.FindSymbolByFqnAsync(sln, targetFqn, null);
                return true;
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                                       $"No symbol with FQN '{targetFqn}' found in solution.",
                                                       StringComparison.Ordinal))
            {
            }
        }

        return false;
    }

    private static IEnumerable<string> GetAlreadyAppliedTargetFqns(RenameCsvRow row)
    {
        if (row.OldFqn is null || row.NewName is null)
            yield break;

        yield return ReplaceLastSegment(row.OldFqn, row.NewName);

        if (!string.IsNullOrWhiteSpace(row.AppliedFqn))
            yield return row.AppliedFqn!;
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

        if (TryGetColumn(map, SourceSymbolColumns, out var oldFqnIndex)
            && TryGetColumn(map, TargetNameColumns, out var newNameIndex))
        {
            return ReadStandardBatchRows(parser, map, oldFqnIndex, newNameIndex);
        }

        var mappings = FindRenameColumnMappings(map);
        if (mappings.Count > 0)
            return ReadMappedBatchRows(parser, map, mappings);

        throw new InvalidOperationException(
            "CSV must contain source_symbol/target_name columns or one or more *_fqn/*_symbol columns with matching *_name_new/*_new_name/*_target_name columns.");
    }

    private List<RenameCsvRow> ReadStandardBatchRows(
        TextFieldParser parser,
        Dictionary<string, int> map,
        int oldFqnIndex,
        int newNameIndex)
    {
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

    private List<RenameCsvRow> ReadMappedBatchRows(
        TextFieldParser parser,
        Dictionary<string, int> map,
        IReadOnlyList<RenameColumnMapping> mappings)
    {
        var hasReviewColumn = map.TryGetValue("review", out var reviewIndex);
        var rows = new List<RenameCsvRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null)
                continue;

            foreach (var mapping in mappings)
            {
                var oldFqn = GetField(fields, mapping.SourceIndex);
                var newName = GetField(fields, mapping.TargetIndex);
                var oldName = mapping.CurrentNameIndex is { } currentNameIndex
                    ? GetField(fields, currentNameIndex)
                    : GetLastSegment(oldFqn);

                if (string.IsNullOrWhiteSpace(oldFqn)
                    || string.IsNullOrWhiteSpace(newName)
                    || string.Equals(oldName, newName, StringComparison.Ordinal)
                    || !seen.Add(oldFqn))
                {
                    continue;
                }

                rows.Add(new RenameCsvRow(
                    oldFqn,
                    newName,
                    hasReviewColumn ? GetField(fields, reviewIndex) : null));
            }
        }

        var targetFqnByOldFqn = rows.ToDictionary(
            row => row.OldFqn!,
            row => ReplaceLastSegment(row.OldFqn!, row.NewName!),
            StringComparer.Ordinal);

        return rows
            .Select(row => row with
            {
                AppliedFqn = ApplyContainingRenames(
                    ReplaceLastSegment(row.OldFqn!, row.NewName!),
                    targetFqnByOldFqn)
            })
            .OrderByDescending(row => GetFqnDepth(row.OldFqn))
            .ToList();
    }

    private static List<RenameColumnMapping> FindRenameColumnMappings(Dictionary<string, int> map)
    {
        var mappings = new List<RenameColumnMapping>();
        foreach (var (sourceColumn, sourceIndex) in map)
        {
            if (!TryGetSourcePrefix(sourceColumn, out var prefix))
                continue;

            var targetColumn = TargetSuffixes
                .Select(suffix => prefix + suffix)
                .FirstOrDefault(map.ContainsKey);
            if (targetColumn is null)
                continue;

            var currentNameColumn = CurrentNameSuffixes
                .Select(suffix => prefix + suffix)
                .FirstOrDefault(map.ContainsKey);

            mappings.Add(new RenameColumnMapping(
                sourceIndex,
                map[targetColumn],
                currentNameColumn is null ? null : map[currentNameColumn]));
        }

        return mappings;
    }

    private static bool TryGetColumn(
        Dictionary<string, int> map,
        IReadOnlyList<string> columns,
        out int index)
    {
        foreach (var column in columns)
            if (map.TryGetValue(column, out index))
                return true;

        index = -1;
        return false;
    }

    private static bool TryGetSourcePrefix(string column, out string prefix)
    {
        foreach (var suffix in SourceSuffixes)
        {
            if (!column.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            prefix = column[..^suffix.Length];
            return !string.IsNullOrWhiteSpace(prefix);
        }

        prefix = "";
        return false;
    }

    private static string ApplyContainingRenames(
        string fqn,
        Dictionary<string, string> targetFqnByOldFqn)
    {
        foreach (var (oldFqn, newFqn) in targetFqnByOldFqn
                     .OrderByDescending(pair => pair.Key.Length))
        {
            if (fqn.StartsWith(oldFqn + ".", StringComparison.Ordinal))
                return newFqn + fqn[oldFqn.Length..];
        }

        return fqn;
    }

    private static string ReplaceLastSegment(string fqn, string newName)
    {
        var parenIndex = fqn.IndexOf('(');
        var searchable = parenIndex >= 0 ? fqn[..parenIndex] : fqn;
        var lastDot = searchable.LastIndexOf('.');
        if (lastDot < 0)
            return newName + (parenIndex >= 0 ? fqn[parenIndex..] : string.Empty);

        return fqn[..(lastDot + 1)] + newName + (parenIndex >= 0 ? fqn[parenIndex..] : string.Empty);
    }

    private static string? GetLastSegment(string? fqn)
    {
        if (string.IsNullOrWhiteSpace(fqn))
            return null;

        var parenIndex = fqn.IndexOf('(');
        var searchable = parenIndex >= 0 ? fqn[..parenIndex] : fqn;
        var lastDot = searchable.LastIndexOf('.');
        return lastDot >= 0 ? searchable[(lastDot + 1)..] : searchable;
    }

    private static int GetFqnDepth(string? fqn)
    {
        return string.IsNullOrWhiteSpace(fqn)
            ? 0
            : fqn.Count(ch => ch == '.');
    }

    internal string? GetField(string[] fields, int index)
    {
        return index >= 0 && index < fields.Length ? fields[index] : null;
    }

    internal sealed record RenameCsvRow(
        string? OldFqn,
        string? NewName,
        string? Review,
        string? AppliedFqn = null);

    private sealed record RenameColumnMapping(
        int SourceIndex,
        int TargetIndex,
        int? CurrentNameIndex);

    private static readonly string[] SourceSymbolColumns =
    [
        "source_symbol",
        "symbol_fqn",
        "old_fqn"
    ];

    private static readonly string[] TargetNameColumns =
    [
        "target_name",
        "new_name"
    ];

    private static readonly string[] SourceSuffixes =
    [
        "_fqn",
        "_symbol"
    ];

    private static readonly string[] TargetSuffixes =
    [
        "_name_new",
        "_new_name",
        "_target_name"
    ];

    private static readonly string[] CurrentNameSuffixes =
    [
        "_name",
        "_current_name",
        "_old_name"
    ];
}
