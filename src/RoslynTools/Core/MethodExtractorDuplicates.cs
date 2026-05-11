using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
///     Finds structurally-matching duplicate statement sequences and rewrites them to calls
///     of the newly-extracted method. Invoked by <see cref="MethodExtractor" /> when
///     <c>--replace-duplicates</c> is set.
/// </summary>
internal static class MethodExtractorDuplicates
{
    /// <summary>
    ///     Backward-compatible overload (no ref/out params) for guard-extraction callers.
    /// </summary>
    internal static Task<Solution> ReplaceDuplicatesAsync(
        Solution extractedSln,
        string filePath,
        int newMethodStartLine,
        int newMethodEndLine,
        string newMethodName,
        IReadOnlyList<ISymbol> orderedParameters,
        IReadOnlyList<ISymbol> returnVars)
    {
        return ReplaceDuplicatesAsync(extractedSln, filePath, newMethodStartLine, newMethodEndLine,
            newMethodName, orderedParameters, [], [], returnVars);
    }

    /// <summary>
    ///     After extraction, find all structural duplicates of the extracted body and replace
    ///     them with calls to the new method. Emits warnings for skipped matches and a summary.
    /// </summary>
    internal static async Task<Solution> ReplaceDuplicatesAsync(
        Solution extractedSln,
        string filePath,
        int newMethodStartLine,
        int newMethodEndLine,
        string newMethodName,
        IReadOnlyList<ISymbol> orderedParameters,
        IReadOnlyList<ISymbol> refParams,
        IReadOnlyList<ISymbol> outParams,
        IReadOnlyList<ISymbol> returnVars)
    {
        // Find all structural matches using the extracted body as the pattern
        var matches = await PatternMatcher.FindMatchesAsync(
            extractedSln,
            filePath,
            newMethodStartLine,
            newMethodEndLine,
            null);

        // Phase 2 blocker fix: Detect cross-file matches and upgrade visibility
        var absPath = Path.GetFullPath(filePath);
        var hasCrossFileMatches = matches.Any(m =>
            !string.Equals(Path.GetFullPath(m.File), absPath, StringComparison.OrdinalIgnoreCase));

        var currentSln = extractedSln;
        if (hasCrossFileMatches)
            // If there are cross-file duplicates, upgrade the extracted method from private to public static
            // so that other files can call it
            currentSln = await UpgradeMethodVisibilityAsync(extractedSln, filePath, newMethodName);

        return await ApplyMatchesAsync(currentSln, filePath, newMethodStartLine, newMethodEndLine, matches,
            newMethodName, orderedParameters, refParams, outParams, returnVars);
    }

    /// <summary>
    ///     Upgrades an extracted method from private to public static visibility.
    ///     This enables cross-file call sites to access the method.
    /// </summary>
    private static async Task<Solution> UpgradeMethodVisibilityAsync(
        Solution solution,
        string filePath,
        string methodName)
    {
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
            .FirstOrDefault(m => m.Identifier.Text == methodName);

        if (methodDecl == null)
            return solution;

        // Replace "private" with "public static"
        var newModifiersList = methodDecl.Modifiers
            .Where(m => !m.IsKind(SyntaxKind.PrivateKeyword))
            .ToList();

        // Add public and static at the beginning
        newModifiersList.Insert(0, SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        newModifiersList.Insert(0, SyntaxFactory.Token(SyntaxKind.PublicKeyword));

        var newMethodDecl = methodDecl.WithModifiers(SyntaxFactory.TokenList(newModifiersList));
        var newRoot = root.ReplaceNode(methodDecl, newMethodDecl);

        return solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
    }

    /// <summary>
    ///     Phase 2-4+6: Regenerate method signature with parameters and rewrite body for parameterized literals,
    ///     identifier variations, and generic type parameters.
    /// </summary>
    private static async Task<Solution> RegenerateForParameterizedVariationsAsync(
        Solution solution,
        string filePath,
        string newMethodName,
        List<List<string>> literalsByMatch,
        HashSet<int> varyingLiteralPositions,
        List<string> patternIdentifiers,
        HashSet<int> varyingIdentifierPositions,
        IReadOnlyDictionary<string, string> typeParamMap)
    {
        if (varyingLiteralPositions.Count == 0 && varyingIdentifierPositions.Count == 0 && typeParamMap.Count == 0)
            return solution;

        if (literalsByMatch.Count == 0 && varyingLiteralPositions.Count > 0)
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

        // Phase 6: Add type parameter list for generic type-arg variations
        var newMethodDecl = methodDecl;
        if (typeParamMap.Count > 0)
        {
            var typeParams = typeParamMap.Values
                .Select(name => SyntaxFactory.TypeParameter(name))
                .ToArray();
            var typeParamList = SyntaxFactory.TypeParameterList(
                SyntaxFactory.SeparatedList(typeParams));
            newMethodDecl = newMethodDecl.WithTypeParameterList(typeParamList);
        }

        // Add parameters for varying literal positions, then varying identifier positions
        // (varyingIdentifierPositions here already excludes type-arg positions — they become type params above)
        newMethodDecl = AddParametersForVariations(newMethodDecl, varyingLiteralPositions, patternIdentifiers,
            varyingIdentifierPositions);

        // Rewrite body to use parameter references for both literals and identifiers
        newMethodDecl = RewriteBodyForLiteralParameters(newMethodDecl, varyingLiteralPositions);
        newMethodDecl = RewriteBodyForIdentifierParameters(newMethodDecl, patternIdentifiers,
            varyingIdentifierPositions, varyingLiteralPositions.Count);

        // Phase 6: Rewrite body to replace type-position identifiers with type parameter names
        if (typeParamMap.Count > 0) newMethodDecl = RewriteBodyForTypeParameters(newMethodDecl, typeParamMap);

        var newRoot = root.ReplaceNode(methodDecl, newMethodDecl);
        return solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
    }

    private static MethodDeclarationSyntax AddParametersForVariations(
        MethodDeclarationSyntax methodDecl,
        HashSet<int> varyingLiteralPositions,
        List<string> patternIdentifiers,
        HashSet<int> varyingIdentifierPositions)
    {
        var currentParams = methodDecl.ParameterList.Parameters.ToList();

        // Add string parameters for varying literal positions
        foreach (var pos in varyingLiteralPositions.OrderBy(p => p))
        {
            var paramName = "param" + pos.ToString(CultureInfo.InvariantCulture);
            var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
                .WithType(SyntaxFactory.ParseTypeName("string")
                    .WithTrailingTrivia(SyntaxFactory.Space));
            currentParams.Add(newParam);
        }

        // Add object parameters for varying identifier positions
        var identParamOffset = varyingLiteralPositions.Count;
        foreach (var pos in varyingIdentifierPositions.OrderBy(p => p))
        {
            var paramName = "param" + (identParamOffset + pos).ToString(CultureInfo.InvariantCulture);
            var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
                .WithType(SyntaxFactory.ParseTypeName("object")
                    .WithTrailingTrivia(SyntaxFactory.Space));
            currentParams.Add(newParam);
        }

        var newParamList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(currentParams));
        return methodDecl.WithParameterList(newParamList);
    }

    private static MethodDeclarationSyntax RewriteBodyForLiteralParameters(
        MethodDeclarationSyntax methodDecl,
        HashSet<int> varyingPositions)
    {
        if (methodDecl.Body == null || varyingPositions.Count == 0)
            return methodDecl;

        var positionToParam = new Dictionary<int, string>();
        foreach (var pos in varyingPositions.OrderBy(p => p))
            positionToParam[pos] = "param" + pos.ToString(CultureInfo.InvariantCulture);

        var rewriter = new LiteralReplacementRewriter(positionToParam);
        var newBody = (BlockSyntax)rewriter.Visit(methodDecl.Body);
        return methodDecl.WithBody(newBody);
    }

    private static MethodDeclarationSyntax RewriteBodyForIdentifierParameters(
        MethodDeclarationSyntax methodDecl,
        List<string> patternIdentifiers,
        HashSet<int> varyingIdentifierPositions,
        int literalParamCount)
    {
        if (methodDecl.Body == null || varyingIdentifierPositions.Count == 0)
            return methodDecl;

        // Build map: original identifier name -> parameter name
        var identifierToParam = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pos in varyingIdentifierPositions.OrderBy(p => p))
            if (pos < patternIdentifiers.Count)
            {
                var originalName = patternIdentifiers[pos];
                var paramName = "param" + (literalParamCount + pos).ToString(CultureInfo.InvariantCulture);
                identifierToParam[originalName] = paramName;
            }

        var rewriter = new IdentifierReplacementRewriter(identifierToParam);
        var newBody = (BlockSyntax)rewriter.Visit(methodDecl.Body);
        return methodDecl.WithBody(newBody);
    }

    /// <summary>
    ///     Phase 6: Rewrites the method body replacing type-position identifier names with
    ///     their assigned type parameter names (e.g. MyType -> T).
    ///     Uses the same IdentifierReplacementRewriter as Phase 2 since the replacement
    ///     is purely name-based — IdentifierNameSyntax nodes are replaced regardless of
    ///     whether they appear in type or value position.
    /// </summary>
    private static MethodDeclarationSyntax RewriteBodyForTypeParameters(
        MethodDeclarationSyntax methodDecl,
        IReadOnlyDictionary<string, string> typeParamMap)
    {
        if (methodDecl.Body == null || typeParamMap.Count == 0)
            return methodDecl;

        var rewriter = new IdentifierReplacementRewriter(
            typeParamMap.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));
        var newBody = (BlockSyntax)rewriter.Visit(methodDecl.Body);
        return methodDecl.WithBody(newBody);
    }

    /// <summary>
    ///     Apply a pre-built match list, replacing each match with a call to
    ///     <paramref name="newMethodName" />. Exposed as <c>internal</c> so tests can
    ///     inject synthetic match lists (including overlapping ones) to verify the
    ///     overlap-warning contract without going through the full MSBuild workspace.
    /// </summary>
    internal static async Task<Solution> ApplyMatchesAsync(
        Solution solution,
        string filePath,
        int sourceStartLine,
        int sourceEndLine,
        IReadOnlyList<PatternMatch> matches,
        string newMethodName,
        IReadOnlyList<ISymbol> orderedParameters,
        IReadOnlyList<ISymbol> returnVars)
    {
        return await ApplyMatchesAsync(solution, filePath, sourceStartLine, sourceEndLine, matches,
            newMethodName, orderedParameters, [], [], returnVars);
    }

    internal static async Task<Solution> ApplyMatchesAsync(
        Solution solution,
        string filePath,
        int sourceStartLine,
        int sourceEndLine,
        IReadOnlyList<PatternMatch> matches,
        string newMethodName,
        IReadOnlyList<ISymbol> orderedParameters,
        IReadOnlyList<ISymbol> refParams,
        IReadOnlyList<ISymbol> outParams,
        IReadOnlyList<ISymbol> returnVars)
    {
        // Phase 1: Analyze literal variations
        var (literalsByMatch, varyingLiteralPositions) =
            await AnalyzeLiteralVariationsAsync(solution, filePath, newMethodName, matches);

        // Phase 2: Analyze identifier variations using PatternMatch.Substitutions.
        // Exclude identifiers already covered by orderedParameters (the existing method params).
        var existingParamNames = orderedParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var (patternIdentifiers, varyingIdentifierPositions) = await AnalyzeIdentifierVariationsAsync(solution,
            filePath, sourceStartLine, sourceEndLine, matches, existingParamNames);

        // Phase 6: Classify varying identifier positions — type-arg positions become generic
        // type parameters; value positions remain as ordinary value parameters.
        var sourceStatements = await LoadSourceStatementsAsync(solution, filePath, sourceStartLine, sourceEndLine);
        var (typeParamNames, effectiveIdentifierPositions) = TypeParameterVariationDetector.ClassifyVaryingPositions(
            patternIdentifiers, varyingIdentifierPositions, sourceStatements);
        var typeParamMap = TypeParameterVariationDetector.AssignTypeParameterNames(typeParamNames);

        // Check if return variable names differ across matches (indicates incompatible structure)
        // In that case, skip parameterization entirely — variable renaming requires synthetic code generation
        // that the current system doesn't support.
        var returnVarNamesMatch =
            CheckReturnVariableNamesConsistent(solution, filePath, sourceStartLine, sourceEndLine, matches, returnVars);

        // If there are any varying positions (literals, identifiers, or type params), regenerate method signature and body
        // Note: PatternMatcher excludes the source file, so matches only contains cross-file duplicates
        // ALSO: Only attempt parameterization if return variable names are consistent (no synthetic renaming needed)
        var currentSln = solution;
        if (returnVarNamesMatch &&
            (varyingLiteralPositions.Count > 0 || effectiveIdentifierPositions.Count > 0 || typeParamMap.Count > 0) &&
            matches.Count > 0)
            currentSln = await RegenerateForParameterizedVariationsAsync(
                currentSln,
                filePath,
                newMethodName,
                literalsByMatch,
                varyingLiteralPositions,
                patternIdentifiers,
                effectiveIdentifierPositions,
                typeParamMap);

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
                    var patternStatements =
                        SelectStatementsInRange(patternRoot, patternMatch.Line, patternMatch.EndLine);
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

                // If parameterizing (literals, identifiers, or type params), accept matches with differences at varying positions
                if (varyingLiteralPositions.Count > 0 || effectiveIdentifierPositions.Count > 0 ||
                    typeParamMap.Count > 0 || patternStringLiterals.SetEquals(matchStringLiterals))
                    filteredMatches.Add(match);
                else
                    RtLog.Warning("{File}:{Line}: string literal semantic mismatch -- skipped", match.File, match.Line);
            }

            matches = filteredMatches;
        }

        // TODO (US-032 Phase 4): After parameterization, update the source file's original call site with the new parameters
        // This requires architectural rework because PatternMatcher excludes the source file from matches,
        // and direct invocation update logic needs to properly handle the source file context.
        // The implementation exists below but needs debugging to properly extract and update the call site.
        //
        // if (returnVarNamesMatch && (varyingLiteralPositions.Count > 0 || effectiveIdentifierPositions.Count > 0 || typeParamMap.Count > 0))
        // {
        //     currentSln = await UpdateSourceFileCallSiteAsync(
        //         currentSln, filePath, newMethodName, sourceStartLine, sourceEndLine,
        //         literalsByMatch, varyingLiteralPositions,
        //         patternIdentifiers, effectiveIdentifierPositions, typeParamMap);
        // }

        var replaced = 0;
        var skipped = 0;

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
                // Overlaps a range already replaced in this file  --  warn and skip.
                skipped++;
                RtLog.Warning("{File}:{Line}: line range {Start}-{End} overlaps an already-replaced range  --  skipped",
                    match.File, match.Line, match.Line, match.EndLine);
                continue;
            }

            var matchIndex = matches.ToList().IndexOf(match);
            // literalsByMatch[0] is source file, literalsByMatch[1..] are cross-file matches
            var matchLiteralIndex = matchIndex + 1;
            var matchLiterals = matchLiteralIndex >= 0 && matchLiteralIndex < literalsByMatch.Count
                ? literalsByMatch[matchLiteralIndex]
                : new List<string>();

            var (success, updatedSln, skipReason) = await TryReplaceMatchAsync(
                currentSln,
                match,
                newMethodName,
                orderedParameters,
                refParams,
                outParams,
                returnVars,
                varyingLiteralPositions,
                matchLiterals,
                patternIdentifiers,
                effectiveIdentifierPositions,
                typeParamMap);

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
                RtLog.Warning("{File}:{Line}: {SkipReason}  --  skipped", match.File, match.Line, skipReason);
            }
        }

        RtConsole.WriteLine(RtOutputContext.Result,
            $"matches found: {matches.Count}, replaced: {replaced}, skipped: {skipped}");

        return currentSln;
    }

    private static async Task<(bool success, Solution? updatedSln, string skipReason)> TryReplaceMatchAsync(
        Solution solution,
        PatternMatch match,
        string newMethodName,
        IReadOnlyList<ISymbol> orderedParameters,
        IReadOnlyList<ISymbol> refParams,
        IReadOnlyList<ISymbol> outParams,
        IReadOnlyList<ISymbol> returnVars,
        HashSet<int>? varyingLiteralPositions = null,
        List<string>? matchLiterals = null,
        List<string>? patternIdentifiers = null,
        HashSet<int>? varyingIdentifierPositions = null,
        IReadOnlyDictionary<string, string>? typeParamMap = null)
    {
        // Build argument list by mapping parameter names through the substitution map
        var args = new List<string>();
        foreach (var param in orderedParameters)
        {
            if (!match.Substitutions.TryGetValue(param.Name, out var substituted))
                return (false, null, $"cannot map '{param.Name}' -- no substitution found");

            // Check if the substituted value is a literal (not an identifier)
            // Literals are things like "42", "\"hello\"", "true" -- they won't be valid
            // identifiers to pass as arguments and indicate the match can't be cleanly replaced.
            if (IsLiteralValue(substituted))
                return (false, null, $"cannot map '{param.Name}' -- substitution is a literal '{substituted}'");

            args.Add(substituted);
        }

        // Append ref arguments — map via substitution if available, else use canonical name
        foreach (var param in refParams)
        {
            var name = match.Substitutions.TryGetValue(param.Name, out var sub) ? sub : param.Name;
            args.Add($"ref {name}");
        }

        // Append out arguments — map via substitution if available, else use canonical name
        foreach (var param in outParams)
        {
            var name = match.Substitutions.TryGetValue(param.Name, out var sub) ? sub : param.Name;
            args.Add($"out {name}");
        }

        // Append literal values for parameterized literal duplicates (Phase 1)
        if (varyingLiteralPositions != null && matchLiterals != null)
            foreach (var pos in varyingLiteralPositions.OrderBy(p => p))
                if (pos < matchLiterals.Count)
                {
                    var literal = matchLiterals[pos];
                    args.Add("\"" + literal + "\"");
                }

        // Append identifier values for parameterized identifier duplicates (Phase 2)
        if (patternIdentifiers != null && varyingIdentifierPositions != null && varyingIdentifierPositions.Count > 0)
            foreach (var pos in varyingIdentifierPositions.OrderBy(p => p))
                if (pos < patternIdentifiers.Count)
                {
                    var patternIdentifier = patternIdentifiers[pos];
                    // Look up what this identifier maps to at the match site
                    var matchIdentifier = match.Substitutions.TryGetValue(patternIdentifier, out var substituted)
                        ? substituted
                        : patternIdentifier;
                    args.Add(matchIdentifier);
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

        // Phase 6: Build explicit type argument list for generic methods
        // typeParamMap maps pattern type name -> type param name (e.g. "MyType" -> "T")
        // We need to emit the match-site concrete types (e.g. "OtherType") in order
        var typeArgs = new List<string>();
        if (typeParamMap != null && typeParamMap.Count > 0)
            foreach (var (patternTypeName, _) in typeParamMap)
            {
                var concreteType = match.Substitutions.TryGetValue(patternTypeName, out var sub)
                    ? sub
                    : patternTypeName;
                typeArgs.Add(concreteType);
            }

        var leadingTrivia = matchedStatements[0].GetLeadingTrivia();
        var callStmt = BuildReplacementCallSite(newMethodName, args, typeArgs, returnVars, match)
            .WithLeadingTrivia(leadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        // Replace the matched statement block with the call
        var block = (BlockSyntax)matchedStatements[0].Parent!;
        var stmts = block.Statements;
        var firstIdx = stmts.IndexOf(matchedStatements[0]);
        var lastIdx = stmts.IndexOf(matchedStatements[^1]);

        for (var i = lastIdx; i >= firstIdx; i--)
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
        List<string> typeArgs,
        IReadOnlyList<ISymbol> returnVars,
        PatternMatch match)
    {
        var argStr = string.Join(", ", args);
        var typeArgSuffix = typeArgs.Count > 0
            ? "<" + string.Join(", ", typeArgs) + ">"
            : string.Empty;
        var call = $"{newMethodName}{typeArgSuffix}({argStr})";

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

    private static string MapSymbolName(ISymbol symbol, PatternMatch match)
    {
        return match.Substitutions.TryGetValue(symbol.Name, out var substituted)
            ? substituted
            : symbol.Name;
    }

    /// <summary>
    ///     Heuristically determines if a value string is a literal (number, string, bool, char, null)
    ///     rather than an identifier that could be passed as an argument.
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

    /// <summary>
    ///     Loads the statements in the given line range from the source file document.
    ///     Returns an empty list if the document or syntax root cannot be resolved.
    /// </summary>
    private static async Task<IReadOnlyList<StatementSyntax>> LoadSourceStatementsAsync(
        Solution solution, string filePath, int startLine, int endLine)
    {
        var absPath = Path.GetFullPath(filePath);
        var doc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, absPath, StringComparison.OrdinalIgnoreCase));
        if (doc == null)
            return [];
        var root = await doc.GetSyntaxRootAsync();
        if (root == null)
            return [];

        // Get statements by line range, being careful about line number conversions
        var allStmts = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(s => s.Parent is BlockSyntax)
            .ToList();

        var result = new List<StatementSyntax>();
        foreach (var stmt in allStmts)
        {
            var span = stmt.GetLocation().GetLineSpan();
            // Check if statement falls within [startLine, endLine] (1-based)
            // Convert to 1-based for comparison: statement at 0-based LinePosition.Line N has 1-based line = N+1
            var stmtStartLine1Based = span.StartLinePosition.Line + 1;
            var stmtEndLine1Based = span.EndLinePosition.Line + 1;

            if (stmtStartLine1Based >= startLine && stmtStartLine1Based <= endLine) result.Add(stmt);
        }

        return result;
    }

    private static List<StatementSyntax> SelectStatementsInRange(SyntaxNode root, int startLine, int endLine)
    {
        var allStmts = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(s => s.Parent is BlockSyntax)
            .ToList();

        var result = new List<StatementSyntax>();
        foreach (var stmt in allStmts)
        {
            var span = stmt.GetLocation().GetLineSpan();
            var stmtStartLine1Based = span.StartLinePosition.Line + 1;
            var stmtEndLine1Based = span.EndLinePosition.Line + 1;

            var matches = stmtStartLine1Based >= startLine && stmtStartLine1Based <= endLine;

            if (matches) result.Add(stmt);
        }

        return result;
    }

    /// <summary>
    ///     Extracts all string literal values from a list of statements.
    /// </summary>
    private static HashSet<string> ExtractStringLiterals(List<StatementSyntax> statements)
    {
        var literals = new HashSet<string>();
        var visitor = new StringLiteralVisitor();

        foreach (var stmt in statements) visitor.Visit(stmt);

        return visitor.Literals;
    }

    /// <summary>
    ///     Phase 1: Analyzes literal variations across all matches.
    ///     Returns (literalsByMatch, varyingPositions).
    /// </summary>
    /// <summary>
    ///     Checks if all matches use the same return variable names as the source pattern.
    ///     Parameterization that requires renaming return variables (e.g., methodOpt->typeOpt)
    ///     requires synthetic code generation and is not currently supported.
    /// </summary>
    private static bool CheckReturnVariableNamesConsistent(
        Solution solution, string sourceFilePath, int sourceStartLine, int sourceEndLine,
        IReadOnlyList<PatternMatch> matches, IReadOnlyList<ISymbol> returnVars)
    {
        if (matches.Count == 0 || returnVars.Count == 0)
            return true;

        // Get the source variable names
        var sourceVarNames = new HashSet<string>(returnVars.Select(v => v.Name), StringComparer.Ordinal);

        // Check each match: the substitution map should map source names to the same names
        // If any source var name maps to a different name in a match, return false
        foreach (var match in matches)
            foreach (var varName in sourceVarNames)
                if (match.Substitutions.TryGetValue(varName, out var mappedName))
                    // If the variable is renamed (e.g., methodOpt -> typeOpt), return false
                    if (!varName.Equals(mappedName, StringComparison.Ordinal))
                        return false;

        return true;
    }

    private static async Task<(List<List<string>>, HashSet<int>)>
        AnalyzeLiteralVariationsAsync(Solution solution, string sourceFilePath, string newMethodName,
            IReadOnlyList<PatternMatch> matches)
    {
        // Extract source file's literals directly from the newly extracted method
        var sourceAbsPath = Path.GetFullPath(sourceFilePath);
        var sourceDoc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, sourceAbsPath, StringComparison.OrdinalIgnoreCase));

        var patternLiterals = new List<string>();
        if (sourceDoc != null)
        {
            var sourceRoot = await sourceDoc.GetSyntaxRootAsync();
            if (sourceRoot != null)
            {
                // Find the method directly by name instead of using line numbers
                var newMethod = sourceRoot.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == newMethodName);

                if (newMethod?.Body != null)
                {
                    var sourceStatements = newMethod.Body.Statements
                        .Where(s => s is not ReturnStatementSyntax)
                        .ToList();

                    if (sourceStatements.Count == 0)
                        sourceStatements = newMethod.Body.Statements.ToList();

                    patternLiterals = ExtractOrderedLiterals(sourceStatements);
                }
            }
        }

        var literalsByMatch = new List<List<string>> { patternLiterals };

        // Add literals from each cross-file match
        foreach (var match in matches)
        {
            var orderedLiterals = await ExtractLiteralsFromMatchAsync(solution, match);
            literalsByMatch.Add(orderedLiterals);
        }

        var varyingPositions = new HashSet<int>();

        for (var pos = 0; pos < patternLiterals.Count; pos++)
        {
            var patternValue = patternLiterals[pos];
            for (var matchIdx = 1; matchIdx < literalsByMatch.Count; matchIdx++)
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
            return new List<string>();

        var matchRoot = await matchDoc.GetSyntaxRootAsync();
        if (matchRoot == null)
            return new List<string>();

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
    ///     Phase 2: Analyzes identifier variations across all matches using PatternMatch.Substitutions.
    ///     Returns (patternIdentifiers, varyingPositions) where patternIdentifiers is the ordered list
    ///     of external identifiers in the source block (excluding those already in orderedParameters),
    ///     and varyingPositions is the set of indices where any match uses a different identifier.
    /// </summary>
    private static async Task<(List<string> patternIdentifiers, HashSet<int> varyingPositions)>
        AnalyzeIdentifierVariationsAsync(
            Solution solution,
            string sourceFilePath,
            int sourceStartLine,
            int sourceEndLine,
            IReadOnlyList<PatternMatch> matches,
            HashSet<string> existingParamNames)
    {
        if (matches.Count == 0)
            return (new List<string>(), new HashSet<int>());

        // Extract ordered external identifiers from the source block
        var sourceAbsPath = Path.GetFullPath(sourceFilePath);
        var sourceDoc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, sourceAbsPath, StringComparison.OrdinalIgnoreCase));

        var rawIdentifiers = new List<string>();
        if (sourceDoc != null)
        {
            var sourceRoot = await sourceDoc.GetSyntaxRootAsync();
            if (sourceRoot != null)
            {
                var sourceStatements = SelectStatementsInRange(sourceRoot, sourceStartLine, sourceEndLine);
                rawIdentifiers = IdentifierVariationDetector.ExtractOrderedIdentifiers(sourceStatements);
            }
        }

        // Filter out identifiers already covered by the method's orderedParameters --
        // those are already mapped through Substitutions in TryReplaceMatchAsync and
        // do not need a new parameter.
        var patternIdentifiers = rawIdentifiers
            .Where(name => !existingParamNames.Contains(name))
            .ToList();

        if (patternIdentifiers.Count == 0)
            return (patternIdentifiers, new HashSet<int>());

        // Build per-match substitution dictionaries from PatternMatch.Substitutions
        // Each match already has a Substitutions dict: patternName -> matchName
        var matchSubstitutions = matches
            .Select(m => m.Substitutions)
            .ToList();

        // Use IdentifierVariationDetector to find which positions vary
        var varyingPositions = IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        return (patternIdentifiers, varyingPositions);
    }

    /// <summary>
    ///     After parameterization, update the source file's original call site with parameterized arguments.
    ///     The extraction process replaces the original statements with a call to the new method.
    ///     When parameterization adds parameters, this call site must be updated to pass those arguments.
    /// </summary>
    private static async Task<Solution> UpdateSourceFileCallSiteAsync(
        Solution solution,
        string filePath,
        string newMethodName,
        int originalStartLine,
        int originalEndLine,
        List<List<string>> literalsByMatch,
        HashSet<int> varyingLiteralPositions,
        List<string> patternIdentifiers,
        HashSet<int> varyingIdentifierPositions,
        IReadOnlyDictionary<string, string> typeParamMap)
    {
        var absPath = Path.GetFullPath(filePath);
        var doc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, absPath, StringComparison.OrdinalIgnoreCase));

        if (doc == null)
            return solution;

        var root = await doc.GetSyntaxRootAsync();
        if (root == null)
            return solution;

        // Extract source file's literal and identifier values
        var sourceStatements = SelectStatementsInRange(root, originalStartLine, originalEndLine);
        if (sourceStatements.Count == 0)
            return solution;

        var sourceLiterals = ExtractOrderedLiterals(sourceStatements);
        var sourceIdentifiers = IdentifierVariationDetector.ExtractOrderedIdentifiers(sourceStatements);

        // Find the method call near the original extraction line
        var callSites = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.Expression is IdentifierNameSyntax id && id.Identifier.Text == newMethodName)
            .ToList();

        if (callSites.Count == 0)
            return solution;

        // Build the argument list from source file values
        var arguments = new List<ExpressionSyntax>();

        foreach (var pos in varyingLiteralPositions.Union(varyingIdentifierPositions).OrderBy(p => p))
        {
            ExpressionSyntax? arg = null;

            if (varyingLiteralPositions.Contains(pos))
            {
                // Literal position: use the source file's literal value
                if (pos < sourceLiterals.Count)
                    arg = SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(sourceLiterals[pos]));
            }
            else
            {
                // Identifier position: use the source file's identifier name
                var idPos = pos - varyingLiteralPositions.Count;
                if (idPos >= 0 && idPos < sourceIdentifiers.Count)
                    arg = SyntaxFactory.IdentifierName(sourceIdentifiers[idPos]);
            }

            if (arg != null)
                arguments.Add(arg);
        }

        // Update the invocation with arguments
        if (arguments.Count > 0)
        {
            var originalCall = callSites[0];
            var newArgList = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(arguments.Select(arg => SyntaxFactory.Argument(arg))));

            var newCall = originalCall.WithArgumentList(newArgList);
            var newRoot = root.ReplaceNode(originalCall, newCall);

            return solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
        }

        return solution;
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
    ///     Replaces specific identifier names in the method body with parameter names.
    ///     Only replaces exact-match simple IdentifierNameSyntax nodes -- skips
    ///     member-access chains, method names, and declaration nodes.
    /// </summary>
    private class IdentifierReplacementRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _identifierToParam;

        public IdentifierReplacementRewriter(Dictionary<string, string> identifierToParam)
        {
            _identifierToParam = identifierToParam;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Skip if this identifier is the name in a member-access (right side of dot)
            if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
                return base.VisitIdentifierName(node);

            // Skip declaration identifiers (variable declarators) -- those are local to the block
            if (node.Parent is VariableDeclaratorSyntax)
                return base.VisitIdentifierName(node);

            var name = node.Identifier.Text;
            if (_identifierToParam.TryGetValue(name, out var paramName))
                return SyntaxFactory.IdentifierName(paramName)
                    .WithTriviaFrom(node);

            return base.VisitIdentifierName(node);
        }
    }

    /// <summary>
    ///     Roslyn syntax walker that collects all literals in traversal order.
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
    ///     Roslyn syntax walker that collects all string literal values.
    /// </summary>
    private class StringLiteralVisitor : CSharpSyntaxWalker
    {
        public HashSet<string> Literals { get; } = new();

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            // Collect string literals (standard and UTF-8)
            if (node.IsKind(SyntaxKind.StringLiteralExpression))
                Literals.Add(node.Token.ValueText);
            else if (node.IsKind(SyntaxKind.Utf8StringLiteralExpression)) Literals.Add(node.Token.ValueText);
            base.VisitLiteralExpression(node);
        }
    }
}
