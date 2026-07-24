using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using RoslynTools.Core;
using RoslynTools.Models;

namespace RoslynTools.Services;

/// <summary>
/// Provides methods for renaming symbols within a solution.
/// </summary>
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

    /// <summary>
    /// Asynchronously renames symbols within a solution.
    /// </summary>
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

    internal async Task<RenameResult> ExecuteAsync(Solution solution, RenameRequest request)
    {
        var relatedPlan = await BuildRelatedRenamePlanAsync(
            solution,
            request.Symbol,
            request.NewName,
            request.Kind);
        var withRelatedEdits = await ApplyRelatedRenameEditsAsync(
            solution,
            relatedPlan,
            request.RelatedEditIds);
        var renamedSolution = await RenameAsync(
            withRelatedEdits,
            request.Symbol,
            request.NewName,
            request.Kind);
        var changedFilePaths = renamedSolution.GetChanges(solution)
            .GetProjectChanges()
            .SelectMany(project => project.GetChangedDocuments())
            .Select(id => renamedSolution.GetDocument(id)?.FilePath)
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (!request.DryRun)
        {
            await _solutionChangeWriter.ApplyChangesAsync(solution, renamedSolution, false);
            ApplyRelatedFileRenames(relatedPlan, request.RelatedEditIds, false);
        }

        return new RenameResult(
            request.Symbol,
            request.NewName,
            changedFilePaths.Length,
            changedFilePaths,
            !request.DryRun,
            request.RelatedEditIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            relatedPlan.Edits);
    }

    internal async Task<RelatedRenamePlan> BuildRelatedRenamePlanAsync(
        Solution sln,
        string symbol,
        string newName,
        string? kind)
    {
        var sym = await _symbols.FindSymbolByFqnAsync(sln, symbol, kind);
        var oldName = sym.Name;
        var edits = new List<RelatedRenameEdit>();

        if (ComputeFileRename(sym, newName) is { } fileRename)
        {
            edits.Add(new RelatedRenameEdit(
                CreateRelatedEditId("file", fileRename.OldPath),
                "file",
                fileRename.OldPath,
                fileRename.NewPath,
                [fileRename.OldPath],
                1,
                false));
        }

        var identifiers = new Dictionary<string, (string NewName, HashSet<string> Paths, int Count)>(
            StringComparer.Ordinal);
        var textual = new List<RelatedRenameEdit>();

        foreach (var project in sln.Projects)
            foreach (var document in project.Documents.Where(document => document.SupportsSyntaxTree))
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var path = document.FilePath ?? document.Name;
                var semanticModel = await document.GetSemanticModelAsync();
                foreach (var token in root.DescendantTokens())
                {
                    if (token.IsKind(SyntaxKind.IdentifierToken)
                        && token.ValueText.Length > oldName.Length
                        && token.ValueText.Contains(oldName, StringComparison.Ordinal))
                    {
                        AddIdentifierCandidate(identifiers, token.ValueText,
                            token.ValueText.Replace(oldName, newName, StringComparison.Ordinal), path);
                    }
                    else if (token.IsKind(SyntaxKind.IdentifierToken)
                             && semanticModel is not null
                             && TryGetDeclaredValueType(semanticModel, token, out var declaredType)
                             && SymbolEqualityComparer.Default.Equals(declaredType, sym))
                    {
                        AddIdentifierCandidate(identifiers, token.ValueText, ToCamelCase(newName), path);
                    }

                    if (token.Parent is LiteralExpressionSyntax literal
                        && literal.IsKind(SyntaxKind.StringLiteralExpression)
                        && token.ValueText.Contains(oldName, StringComparison.Ordinal))
                    {
                        var replacement = token.ValueText.Replace(oldName, newName, StringComparison.Ordinal);
                        textual.Add(CreateTextEdit("string", path, token.SpanStart, token.ValueText, replacement, false));
                    }

                    foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
                    {
                        if (!IsComment(trivia) || !trivia.ToFullString().Contains(oldName, StringComparison.Ordinal))
                            continue;

                        var oldText = trivia.ToFullString();
                        textual.Add(CreateTextEdit(
                            "comment",
                            path,
                            trivia.SpanStart,
                            oldText,
                            oldText.Replace(oldName, newName, StringComparison.Ordinal),
                            true));
                    }
                }
            }

        edits.AddRange(identifiers
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new RelatedRenameEdit(
                CreateRelatedEditId("identifier", pair.Key),
                "identifier",
                pair.Key,
                pair.Value.NewName,
                pair.Value.Paths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                pair.Value.Count,
                false)));
        edits.AddRange(textual.OrderBy(edit => edit.Id, StringComparer.Ordinal));

        return new RelatedRenamePlan(edits);
    }

    internal async Task<Solution> ApplyRelatedRenameEditsAsync(
        Solution solution,
        RelatedRenamePlan plan,
        IReadOnlySet<string> selectedIds)
    {
        var selected = plan.Edits
            .Where(edit => selectedIds.Contains(edit.Id) && edit.Kind != "file")
            .ToList();

        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            var path = document.FilePath ?? document.Name;
            var documentEdits = selected.Where(edit => edit.Paths.Contains(path, StringComparer.Ordinal)).ToList();
            if (documentEdits.Count == 0)
                continue;

            var root = await document.GetSyntaxRootAsync();
            if (root is null)
                continue;

            var identifierEdits = documentEdits
                .Where(edit => edit.Kind == "identifier")
                .ToDictionary(edit => edit.OldText, edit => edit.NewText, StringComparer.Ordinal);
            var textEdits = documentEdits
                .Where(edit => edit.Kind is "string" or "comment")
                .ToDictionary(edit => edit.Id, StringComparer.Ordinal);

            var rewritten = root.ReplaceTokens(
                root.DescendantTokens(),
                (original, current) => RewriteRelatedToken(current, path, identifierEdits, textEdits));
            solution = solution.WithDocumentSyntaxRoot(document.Id, rewritten);
        }

        return solution;
    }

    internal void ApplyRelatedFileRenames(
        RelatedRenamePlan plan,
        IReadOnlySet<string> selectedIds,
        bool dryRun)
    {
        foreach (var edit in plan.Edits.Where(edit => edit.Kind == "file" && selectedIds.Contains(edit.Id)))
            ApplyFileRename((edit.OldText, edit.NewText), dryRun);
    }

    private static SyntaxToken RewriteRelatedToken(
        SyntaxToken token,
        string path,
        Dictionary<string, string> identifiers,
        Dictionary<string, RelatedRenameEdit> textEdits)
    {
        if (token.IsKind(SyntaxKind.IdentifierToken)
            && identifiers.TryGetValue(token.ValueText, out var identifierReplacement))
            token = SyntaxFactory.Identifier(token.LeadingTrivia, identifierReplacement, token.TrailingTrivia);

        if (token.Parent is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var id = CreateRelatedEditId("string", path, token.SpanStart);
            if (textEdits.TryGetValue(id, out var edit))
                token = SyntaxFactory.Literal(
                    token.LeadingTrivia,
                    SymbolDisplay.FormatLiteral(edit.NewText, quote: true),
                    edit.NewText,
                    token.TrailingTrivia);
        }

        token = token.WithLeadingTrivia(RewriteCommentTrivia(token.LeadingTrivia, path, textEdits))
            .WithTrailingTrivia(RewriteCommentTrivia(token.TrailingTrivia, path, textEdits));
        return token;
    }

    private static SyntaxTriviaList RewriteCommentTrivia(
        SyntaxTriviaList triviaList,
        string path,
        Dictionary<string, RelatedRenameEdit> edits)
    {
        return SyntaxFactory.TriviaList(triviaList.Select(trivia =>
        {
            var id = CreateRelatedEditId("comment", path, trivia.SpanStart);
            return edits.TryGetValue(id, out var edit)
                ? SyntaxFactory.Comment(edit.NewText)
                : trivia;
        }));
    }

    private static void AddIdentifierCandidate(
        Dictionary<string, (string NewName, HashSet<string> Paths, int Count)> candidates,
        string oldName,
        string newName,
        string path)
    {
        if (!candidates.TryGetValue(oldName, out var candidate))
            candidate = (newName, new HashSet<string>(StringComparer.Ordinal), 0);
        candidate.Paths.Add(path);
        candidates[oldName] = (candidate.NewName, candidate.Paths, candidate.Count + 1);
    }

    private static RelatedRenameEdit CreateTextEdit(
        string kind,
        string path,
        int spanStart,
        string oldText,
        string newText,
        bool prose)
    {
        return new RelatedRenameEdit(
            CreateRelatedEditId(kind, path, spanStart),
            kind,
            oldText,
            newText,
            [path],
            1,
            prose);
    }

    private static string CreateRelatedEditId(string kind, string value, int? spanStart = null)
    {
        var normalized = value.Replace('\\', '/');
        return spanStart is null ? $"{kind}:{normalized}" : $"{kind}:{normalized}:{spanStart}";
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
               || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
               || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
               || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }

    private static bool TryGetDeclaredValueType(
        SemanticModel semanticModel,
        SyntaxToken token,
        out ITypeSymbol? type)
    {
        var declared = token.Parent switch
        {
            VariableDeclaratorSyntax variable => semanticModel.GetDeclaredSymbol(variable),
            ParameterSyntax parameter => semanticModel.GetDeclaredSymbol(parameter),
            PropertyDeclarationSyntax property => semanticModel.GetDeclaredSymbol(property),
            _ => null
        };

        type = declared switch
        {
            ILocalSymbol local => local.Type,
            IFieldSymbol field => field.Type,
            IParameterSymbol parameter => parameter.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };
        return type is not null;
    }

    private static string ToCamelCase(string value)
    {
        return value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
    }

    /// <summary>
    /// Asynchronously renames a single symbol within a solution.
    /// </summary>
    internal async Task ExecuteSingleAsync(
        Solution sln,
        string symbol,
        string newName,
        string? kind,
        bool dryRun)
    {
        await ExecuteAsync(
            sln,
            new RenameRequest(
                symbol,
                newName,
                kind,
                dryRun,
                new HashSet<string>(StringComparer.Ordinal)));
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
            RtConsole.WriteLine(RtOutputContext.Diagnostic,
                $"[dry-run] Would rename: {rename.OldPath} -> {rename.NewPath}");
            return;
        }

        if (File.Exists(rename.NewPath))
        {
            _logger.RtWarning("Target file already exists: {Path} — file not renamed.", rename.NewPath);
            return;
        }

        File.Move(rename.OldPath, rename.NewPath);
        RtConsole.WriteLine(RtOutputContext.Diagnostic, $"Renamed file: {rename.OldPath} -> {rename.NewPath}");
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

    /// <summary>
    /// Reads batch rows for renaming operations within a solution.
    /// </summary>
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
            return ReadStandardBatchRows(parser, map, oldFqnIndex, newNameIndex);

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
                    continue;

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
            if (fqn.StartsWith(oldFqn + ".", StringComparison.Ordinal))
                return newFqn + fqn[oldFqn.Length..];

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

    /// <summary>
    /// Gets the field at a specific index for renaming operations within a solution.
    /// </summary>
    internal string? GetField(string[] fields, int index)
    {
        return index >= 0 && index < fields.Length ? fields[index] : null;
    }

    /// <summary>
    /// A named type used for renaming CSV rows within a solution.
    /// </summary>
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
