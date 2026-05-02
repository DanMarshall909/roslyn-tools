using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
/// Finds structurally-matching duplicate statement sequences and rewrites them to calls
/// of the newly-extracted method. Invoked by <see cref="MethodExtractor"/> when
/// <c>--replace-duplicates</c> is set.
/// </summary>
internal static class MethodExtractorDuplicates
{
    /// <summary>
    /// After extraction, find all structural duplicates of the extracted body and replace
    /// them with calls to the new method. Emits warnings for skipped matches and a summary.
    /// </summary>
    internal static async Task<Solution> ReplaceDuplicatesAsync(
        Solution extractedSln,
        string filePath,
        int newMethodStartLine,
        int newMethodEndLine,
        string newMethodName,
        IReadOnlyList<ISymbol> orderedParameters,
        IReadOnlyList<ISymbol> returnVars)
    {
        // Find all structural matches using the extracted body as the pattern
        var matches = await PatternMatcher.FindMatchesAsync(
            extractedSln,
            filePath,
            newMethodStartLine,
            newMethodEndLine,
            scope: null);

        return await ApplyMatchesAsync(extractedSln, matches, newMethodName, orderedParameters, returnVars);
    }

    /// <summary>
    /// Phase 2-4: Regenerate method signature with parameters and rewrite body for parameterized literals.
    /// </summary>
    private static async Task<Solution> RegenerateForParameterizedLiteralsAsync(
        Solution solution,
        string filePath,
        string newMethodName,
        List<List<string>> literalsByMatch,
        HashSet<int> varyingPositions)
    {
        if (varyingPositions.Count == 0 || literalsByMatch.Count == 0)
            return solution;

        var absPath = Path.GetFullPath(filePath);
        var doc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, absPath, StringComparison.OrdinalIgnoreCase));

        if (doc == null)
            return solution;

        var root = await doc.GetSyntaxRootAsync();
        if (root == null)
            return solution;

        var methodDecl = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == newMethodName);

        if (methodDecl == null)
            return solution;

        // Add parameters for varying positions
        var newMethodDecl = AddParametersForLiterals(methodDecl, varyingPositions);

        // Rewrite body to use parameter references
        newMethodDecl = RewriteBodyForParameters(newMethodDecl, varyingPositions);

        var newRoot = root.ReplaceNode(methodDecl, newMethodDecl);
        return solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
    }

    private static MethodDeclarationSyntax AddParametersForLiterals(
        MethodDeclarationSyntax methodDecl,
        HashSet<int> varyingPositions)
    {
        var currentParams = methodDecl.ParameterList.Parameters.ToList();

        foreach (var pos in varyingPositions.OrderBy(p => p))
        {
            var paramName = "param" + pos.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
                .WithType(SyntaxFactory.ParseTypeName("string"));
            currentParams.Add(newParam);
        }

        var newParamList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(currentParams));
        return methodDecl.WithParameterList(newParamList);
    }

    private static MethodDeclarationSyntax RewriteBodyForParameters(
        MethodDeclarationSyntax methodDecl,
        HashSet<int> varyingPositions)
    {
        if (methodDecl.Body == null)
            return methodDecl;

        var positionToParam = new Dictionary<int, string>();
        foreach (var pos in varyingPositions.OrderBy(p => p))
            positionToParam[pos] = "param" + pos.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var rewriter = new LiteralReplacementRewriter(positionToParam);
        var newBody = (BlockSyntax)rewriter.Visit(methodDecl.Body);
        return methodDecl.WithBody(newBody);
    }

    private class LiteralReplacementRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<int, string> _positionToParam;
        private int _literalIndex = -1;

        public LiteralReplacementRewriter(Dictionary<int, string> positionToParam)
        {
            _positionToParam = positionToParam;
        }

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            _literalIndex++;
            if (_positionToParam.TryGetValue(_literalIndex, out var paramName))
                return SyntaxFactory.IdentifierName(paramName);
            return base.VisitLiteralExpression(node);
        }
    }

    /// <summary>
    /// Apply a pre-built match list, replacing each match with a call to
    /// <paramref name="newMethodName"/>. Exposed as <c>internal</c> so tests can
    /// inject synthetic match lists (including overlapping ones) to verify the
    /// overlap-warning contract without going through the full MSBuild workspace.
    /// </summary>
    internal static async Task<Solution> ApplyMatchesAsync(
        Solution solution,
        IReadOnlyList<PatternMatch> matches,
        string newMethodName,
        IReadOnlyList<ISymbol> orderedParameters,
        IReadOnlyList<ISymbol> returnVars)
    {
        // Phase 1: Analyze literal variations
        var (literalsByMatch, varyingPositions) = await AnalyzeLiteralVariationsAsync(solution, matches);

        // If there are varying literals, regenerate method signature and body
        var currentSln = solution;
        if (varyingPositions.Count > 0 && matches.Count > 1)
        {
            var firstMatch = matches[0];
            currentSln = await RegenerateForParameterizedLiteralsAsync(
                currentSln,
                firstMatch.File,
                newMethodName,
                literalsByMatch,
                varyingPositions);
        }

        // Validate semantic equivalence: filter matches by string literal comparison
        // For parameterized cases (varyingPositions.Count > 0), allow different literals at varying positions
        if (matches.Count > 0)
        {
            var patternMatch = matches[0];
            var patternAbsPath = Path.GetFullPath(patternMatch.File);
            var patternDoc = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => string.Equals(d.FilePath, patternAbsPath, StringComparison.OrdinalIgnoreCase));

            var patternStringLiterals = new HashSet<string>();
            if (patternDoc != null)
            {
                var patternRoot = await patternDoc.GetSyntaxRootAsync();
                if (patternRoot != null)
                {
                    var patternStatements = SelectStatementsInRange(patternRoot, patternMatch.Line, patternMatch.EndLine);
                    patternStringLiterals = ExtractStringLiterals(patternStatements);
                }
            }

            // Filter: keep only matches with identical string literals (unless parameterizing)
            var filteredMatches = new List<PatternMatch>();
            foreach (var match in matches)
            {
                var matchAbsPath = Path.GetFullPath(match.File);
                var matchDoc = solution.Projects
                    .SelectMany(p => p.Documents)
                    .FirstOrDefault(d => string.Equals(d.FilePath, matchAbsPath, StringComparison.OrdinalIgnoreCase));

                if (matchDoc == null)
                {
                    filteredMatches.Add(match);
                    continue;
                }

                var matchRoot = await matchDoc.GetSyntaxRootAsync();
                if (matchRoot == null)
                {
                    filteredMatches.Add(match);
                    continue;
                }

                var matchStatements = SelectStatementsInRange(matchRoot, match.Line, match.EndLine);
                var matchStringLiterals = ExtractStringLiterals(matchStatements);

                // If parameterizing, accept matches with different literals at varying positions
                if (varyingPositions.Count > 0 || patternStringLiterals.SetEquals(matchStringLiterals))
                {
                    filteredMatches.Add(match);
                }
                else
                {
                    RtLog.Warning("{File}:{Line}: string literal semantic mismatch — skipped", match.File, match.Line);
                }
            }

            matches = filteredMatches;
        }

        int replaced = 0;
        int skipped = 0;

        // Process matches in an order that keeps line numbers stable as we rewrite.
        // - Group by file so cross-file matches don't affect each other.
        // - Within each file, apply replacements bottom-up (largest Line first) so earlier
        //   line numbers stay correct when later ones shrink.
        // - Overlapping matches within the same file are skipped with a warning.
        //   In practice PatternMatcher only returns exact same-count structural matches so
        //   overlaps cannot arise from it; this guard is defensive for future callers.
        var orderedMatches = matches
            .GroupBy(m => Path.GetFullPath(m.File), StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.OrderByDescending(m => m.Line));

        var claimedRanges = new Dictionary<string, List<(int Start, int End)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in orderedMatches)
        {
            var absPath = Path.GetFullPath(match.File);
            if (!claimedRanges.TryGetValue(absPath, out var ranges))
                claimedRanges[absPath] = ranges = new List<(int, int)>();

            if (ranges.Any(r => match.Line <= r.End && match.EndLine >= r.Start))
            {
                // Overlaps a range already replaced in this file â€” warn and skip.
                skipped++;
                RtLog.Warning("{File}:{Line}: line range {Start}-{End} overlaps an already-replaced range â€” skipped",
                    match.File, match.Line, match.Line, match.EndLine);
                continue;
            }

            var matchIndex = matches.ToList().IndexOf(match);
            var matchLiterals = matchIndex >= 0 && matchIndex < literalsByMatch.Count
                ? literalsByMatch[matchIndex]
                : new List<string>();

            var (success, updatedSln, skipReason) = await TryReplaceMatchAsync(
                currentSln,
                match,
                newMethodName,
                orderedParameters,
                returnVars,
                varyingPositions,
                matchLiterals);

            if (success)
            {
                replaced++;
                ranges.Add((match.Line, match.EndLine));
                if (updatedSln != null)
                    currentSln = updatedSln;
            }
            else
            {
                skipped++;
                RtLog.Warning("{File}:{Line}: {SkipReason} â€” skipped", match.File, match.Line, skipReason);
            }
        }

        RtConsole.WriteLine(RtOutputContext.Result, $"matches found: {matches.Count}, replaced: {replaced}, skipped: {skipped}");

        return currentSln;
    }

    private static async Task<(bool success, Solution? updatedSln, string skipReason)> TryReplaceMatchAsync(
        Solution solution,
        PatternMatch match,
        string newMethodName,
        IReadOnlyList<ISymbol> orderedParameters,
        IReadOnlyList<ISymbol> returnVars, HashSet<int>? varyingPositions = null, List<string>? matchLiterals = null)
    {
        // Build argument list by mapping parameter names through the substitution map
        var args = new List<string>();
        foreach (var param in orderedParameters)
        {
            if (!match.Substitutions.TryGetValue(param.Name, out var substituted))
                return (false, null, $"cannot map '{param.Name}' â€” no substitution found");

            // Check if the substituted value is a literal (not an identifier)
            // Literals are things like "42", "\"hello\"", "true" â€” they won't be valid
            // identifiers to pass as arguments and indicate the match can't be cleanly replaced.
            if (IsLiteralValue(substituted))
                return (false, null, $"cannot map '{param.Name}' â€” substitution is a literal '{substituted}'");

            args.Add(substituted);
        }

        // Append literal values for parameterized duplicates
        if (varyingPositions != null && matchLiterals != null)
        {
            foreach (var pos in varyingPositions.OrderBy(p => p))
            {
                if (pos < matchLiterals.Count)
                {
                    var literal = matchLiterals[pos];
                    args.Add("\"" + literal + "\"");
                }
            }
        }

        // Find the document containing the match
        var matchAbsPath = Path.GetFullPath(match.File);
        var doc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, matchAbsPath, StringComparison.OrdinalIgnoreCase));

        if (doc == null)
            return (false, null, "document not found in solution");

        var root = await doc.GetSyntaxRootAsync();
        if (root == null)
            return (false, null, "failed to get syntax root");

        // Find the matched statements by line range
        var matchedStatements = SelectStatementsInRange(root, match.Line, match.EndLine);
        if (matchedStatements.Count == 0)
            return (false, null, "matched statements not found in document");

        var leadingTrivia = matchedStatements[0].GetLeadingTrivia();
        var callStmt = BuildReplacementCallSite(newMethodName, args, returnVars, match)
            .WithLeadingTrivia(leadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        // Replace the matched statement block with the call
        var block = (BlockSyntax)matchedStatements[0].Parent!;
        var stmts = block.Statements;
        int firstIdx = stmts.IndexOf(matchedStatements[0]);
        int lastIdx = stmts.IndexOf(matchedStatements[^1]);

        for (int i = lastIdx; i >= firstIdx; i--)
            stmts = stmts.RemoveAt(i);

        stmts = stmts.Insert(firstIdx, callStmt);
        var newBlock = block.WithStatements(stmts);
        var newRoot = root.ReplaceNode(block, newBlock);

        var updatedSln = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
        return (true, updatedSln, string.Empty);
    }

    private static StatementSyntax BuildReplacementCallSite(
        string newMethodName,
        IReadOnlyList<string> args,
        IReadOnlyList<ISymbol> returnVars,
        PatternMatch match)
    {
        var argStr = string.Join(", ", args);
        var call = $"{newMethodName}({argStr})";

        if (returnVars.Count == 0)
            return SyntaxFactory.ParseStatement($"{call};");

        if (returnVars.Count == 1)
        {
            var returnName = MapSymbolName(returnVars[0], match);
            return SyntaxFactory.ParseStatement($"var {returnName} = {call};");
        }

        var returnNames = string.Join(", ", returnVars.Select(v => MapSymbolName(v, match)));
        return SyntaxFactory.ParseStatement($"var ({returnNames}) = {call};");
    }

    private static string MapSymbolName(ISymbol symbol, PatternMatch match) =>
        match.Substitutions.TryGetValue(symbol.Name, out var substituted)
            ? substituted
            : symbol.Name;

    /// <summary>
    /// Heuristically determines if a value string is a literal (number, string, bool, char, null)
    /// rather than an identifier that could be passed as an argument.
    /// </summary>
    private static bool IsLiteralValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;

        // null / true / false keywords
        if (value is "null" or "true" or "false") return true;

        // Numeric literal: starts with digit or '-' followed by digit
        if (char.IsDigit(value[0])) return true;
        if (value.Length > 1 && value[0] == '-' && char.IsDigit(value[1])) return true;

        // String literal
        if (value.StartsWith('"') || value.StartsWithOrdinal("@\"") || value.StartsWithOrdinal("$\"")) return true;

        // Char literal
        if (value.StartsWith('\'')) return true;

        return false;
    }

    private static List<StatementSyntax> SelectStatementsInRange(SyntaxNode root, int startLine, int endLine)
        => root.DescendantNodes()
               .OfType<StatementSyntax>()
               .Where(s => s.Parent is BlockSyntax)
               .Where(s =>
               {
                   var span = s.GetLocation().GetLineSpan();
                   return span.StartLinePosition.Line >= startLine - 1
                       && span.EndLinePosition.Line <= endLine - 1;
               })
               .ToList();

    /// <summary>
    /// Extracts all string literal values from a list of statements.
    /// </summary>
    private static HashSet<string> ExtractStringLiterals(List<StatementSyntax> statements)
    {
        var literals = new HashSet<string>();
        var visitor = new StringLiteralVisitor();
        
        foreach (var stmt in statements)
        {
            visitor.Visit(stmt);
        }
        
        return visitor.Literals;
    }

    /// <summary>
    /// Phase 1: Analyzes literal variations across all matches.
    /// Returns (literalsByMatch, varyingPositions).
    /// </summary>
    private static async Task<(List<List<string>>, HashSet<int>)>
        AnalyzeLiteralVariationsAsync(Solution solution, IReadOnlyList<PatternMatch> matches)
    {
        if (matches.Count == 0)
            return (new(), new());

        var literalsByMatch = new List<List<string>>();
        foreach (var match in matches)
        {
            var orderedLiterals = await ExtractLiteralsFromMatchAsync(solution, match);
            literalsByMatch.Add(orderedLiterals);
        }

        var patternLiterals = literalsByMatch.Count > 0 ? literalsByMatch[0] : new();
        var varyingPositions = new HashSet<int>();

        for (int pos = 0; pos < patternLiterals.Count; pos++)
        {
            var patternValue = patternLiterals[pos];
            for (int matchIdx = 1; matchIdx < literalsByMatch.Count; matchIdx++)
            {
                var matchLiterals = literalsByMatch[matchIdx];
                if (pos >= matchLiterals.Count || matchLiterals[pos] != patternValue)
                {
                    varyingPositions.Add(pos);
                    break;
                }
            }
        }

        return (literalsByMatch, varyingPositions);
    }

    private static async Task<List<string>> ExtractLiteralsFromMatchAsync(
        Solution solution, PatternMatch match)
    {
        var matchAbsPath = Path.GetFullPath(match.File);
        var matchDoc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, matchAbsPath, StringComparison.OrdinalIgnoreCase));

        if (matchDoc == null)
            return new();

        var matchRoot = await matchDoc.GetSyntaxRootAsync();
        if (matchRoot == null)
            return new();

        var matchStatements = SelectStatementsInRange(matchRoot, match.Line, match.EndLine);
        return ExtractOrderedLiterals(matchStatements);
    }

    private static List<string> ExtractOrderedLiterals(List<StatementSyntax> statements)
    {
        var literals = new List<string>();
        var visitor = new OrderedLiteralVisitor();
        foreach (var stmt in statements)
            visitor.Visit(stmt);
        return visitor.Literals;
    }

    /// <summary>
    /// Roslyn syntax walker that collects all literals in traversal order.
    /// </summary>
    private class OrderedLiteralVisitor : CSharpSyntaxWalker
    {
        public List<string> Literals { get; } = new();

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            Literals.Add(node.Token.ValueText);
            base.VisitLiteralExpression(node);
        }
    }

    /// <summary>
    /// Roslyn syntax walker that collects all string literal values.
    /// </summary>
    private class StringLiteralVisitor : CSharpSyntaxWalker
    {
        public HashSet<string> Literals { get; } = new();

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            // Collect string literals (standard and UTF-8)
            if (node.IsKind(SyntaxKind.StringLiteralExpression))
            {
                Literals.Add(node.Token.ValueText);
            }
            else if (node.IsKind(SyntaxKind.Utf8StringLiteralExpression))
            {
                Literals.Add(node.Token.ValueText);
            }
            base.VisitLiteralExpression(node);
        }
    }
}

