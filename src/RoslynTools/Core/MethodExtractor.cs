using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynTools.Core;

/// <summary>
/// A class used to extract methods from the code.
/// </summary>
internal static class MethodExtractor
{
    /// <summary>
    ///     Extracts statements within <paramref name="startLine" />–<paramref name="endLine" /> (1-based, inclusive)
    ///     from the method that contains them, placing a new private method named <paramref name="newMethodName" />
    ///     at the specified position within the same type.
    ///     If the selected statements form a guard clause (if (cond) return value;) the extractor
    ///     automatically unwraps the pattern: the new method returns bool and the call site becomes
    ///     <c>if (NewMethod(...)) return value;</c>.
    /// </summary>
    internal static async Task<Solution> ExtractAsync(
        Solution solution,
        string filePath,
        string newMethodName,
        int startLine,
        int endLine,
        string? insertAfter = null,
        string? insertBefore = null,
        bool replaceDuplicates = true,
        ILogger? logger = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startLine, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(endLine, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startLine, endLine);

        if (insertAfter != null && insertBefore != null)
            throw new InvalidOperationException("--after and --before are mutually exclusive.");

        var (doc, root, model, selected) = await FindDocumentAndStatements(solution, filePath, startLine, endLine);

        // ── Guard-pattern unwinding ───────────────────────────────────────────
        var cfClass = ControlFlowUnwinder.Classify(selected);
        if (cfClass.Strategy == ControlFlowStrategy.ExtractAsBoolGuard)
            return await ExtractGuardAsync(
                solution, doc, root, model, selected, newMethodName,
                insertAfter, insertBefore, replaceDuplicates, filePath, logger);

        // ── Standard extraction path (no control-flow escape) ─────────────────
        var flow = ValidateDataFlow(model, selected);
        var (orderedParams, refParams, outParams, returnVars) = OrderParameters(flow, selected, model);
        var (containingMethod, containingType, isStatic) = FindContainingContext(selected);

        // ── 7. Build new method ───────────────────────────────────────────────
        var newMethod = MethodExtractorBuilder.BuildNewMethod(newMethodName, orderedParams, refParams, outParams,
            returnVars, selected, model, isStatic);

        // ── 8. Build call site ────────────────────────────────────────────────
        var callSite = MethodExtractorBuilder.BuildCallSite(newMethodName, orderedParams, refParams, outParams,
            returnVars, selected.First());

        // ── 9. Splice call site into the original block ───────────────────────
        var newContainingMethod = SpliceCallSite(selected, callSite, containingMethod);

        // ── 10. Replace the containing method in the type, then insert the new method ──
        var finalType = InsertNewMethod(containingType, containingMethod, newContainingMethod, newMethod, insertAfter,
            insertBefore);
        var newRoot = root.ReplaceNode(containingType, finalType);

        var extractedSln = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);

        // Guard: the rewrite must have produced at least one changed document.
        var changedDocIds = extractedSln.GetChanges(solution)
            .GetProjectChanges()
            .SelectMany(p => p.GetChangedDocuments())
            .ToList();
        if (changedDocIds.Count == 0)
            throw new InvalidOperationException(
                "extract-method: internal error — the AST rewrite produced no document changes. " +
                "This is a bug; please report it with the file and line range.");

        if (!replaceDuplicates)
            return extractedSln;

        // ── 11. Find and replace structural duplicates ────────────────────────
        var (bodyStartLine, bodyEndLine) = await FindNewMethodBodyRange(extractedSln, filePath, newMethodName);
        return await MethodExtractorDuplicates.ReplaceDuplicatesAsync(
            extractedSln,
            filePath,
            bodyStartLine,
            bodyEndLine,
            newMethodName,
            orderedParams,
            refParams,
            outParams,
            returnVars,
            logger);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Guard-pattern extraction: if (cond) return [value]; → bool NewMethod(params)
    // ────────────────────────────────────────────────────────────────────────────

    private static async Task<Solution> ExtractGuardAsync(
        Solution solution,
        Document doc,
        SyntaxNode root,
        SemanticModel model,
        List<StatementSyntax> selected,
        string newMethodName,
        string? insertAfter,
        string? insertBefore,
        bool replaceDuplicates,
        string filePath,
        ILogger? logger)
    {
        // The guard must be a single if-statement (could be 1 stmt or multi-stmt with void guards)
        // For a single if-statement guard:
        var ifStmt = selected.Count == 1
            ? selected[0] as IfStatementSyntax
            : null;

        if (ifStmt == null)
            // Multi-statement void guard block — fall back to standard extraction error
            throw new InvalidOperationException(
                "Cannot extract statements that contain return statements. " +
                "Multi-statement guard blocks with void returns are not yet auto-extractable.");

        // Collect parameters that flow into the condition from outside
        var flow = model.AnalyzeDataFlow(ifStmt);
        var (containingMethod, containingType, isStatic) = FindContainingContext(selected);

        // Get the parameters from the condition expression
        var conditionIdentifiers = ifStmt.Condition
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Select(id => model.GetSymbolInfo(id).Symbol)
            .OfType<IParameterSymbol>()
            .Distinct<ISymbol>(SymbolEqualityComparer.Default)
            .ToList();

        // Also include local variables from DataFlowsIn
        var flowAnalysis = model.AnalyzeDataFlow(ifStmt.Condition);
        var dataFlowParams = flowAnalysis?.DataFlowsIn
            .Where(s => s is IParameterSymbol or ILocalSymbol)
            .ToList() ?? new List<ISymbol>();

        // Order parameters by first appearance in condition
        var seenParams = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var orderedParams = new List<ISymbol>();
        foreach (var id in ifStmt.Condition
                     .DescendantNodesAndSelf()
                     .OfType<IdentifierNameSyntax>()
                     .OrderBy(id => id.SpanStart))
        {
            var sym = model.GetSymbolInfo(id).Symbol;
            if (sym == null) continue;
            var match = dataFlowParams.FirstOrDefault(p => SymbolEqualityComparer.Default.Equals(p, sym));
            if (match != null && seenParams.Add(match))
                orderedParams.Add(match);
        }

        // Determine the return value from the guard body
        var returnExpr = ExtractReturnExpression(ifStmt);

        // Build the new bool-returning method:
        // private [static] bool NewMethod(params) { return condition; }
        var newMethod = BuildBoolGuardMethod(
            newMethodName, orderedParams, ifStmt.Condition, isStatic, selected.First());

        // Build the call site: if (NewMethod(args)) return value;
        var callSite = BuildGuardCallSite(
            newMethodName, orderedParams, returnExpr, selected.First());

        // Splice call site into the original method
        var newContainingMethod = SpliceCallSite(selected, callSite, containingMethod);

        // Insert the new method
        var finalType = InsertNewMethod(
            containingType, containingMethod, newContainingMethod,
            newMethod, insertAfter, insertBefore);

        var newRoot = root.ReplaceNode(containingType, finalType);
        var extractedSln = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);

        // Verify changes produced
        var changedDocIds = extractedSln.GetChanges(solution)
            .GetProjectChanges()
            .SelectMany(p => p.GetChangedDocuments())
            .ToList();
        if (changedDocIds.Count == 0)
            throw new InvalidOperationException(
                "extract-method: guard extraction produced no document changes.");

        if (!replaceDuplicates)
            return extractedSln;

        // Find and replace structural duplicates
        var (bodyStartLine, bodyEndLine) = await FindNewMethodBodyRange(extractedSln, filePath, newMethodName);
        var emptyReturnVars = new List<ISymbol>();
        return await MethodExtractorDuplicates.ReplaceDuplicatesAsync(
            extractedSln,
            filePath,
            bodyStartLine,
            bodyEndLine,
            newMethodName,
            orderedParams,
            emptyReturnVars,
            logger);
    }

    /// <summary>
    ///     Extracts the return expression (or null for void returns) from a guard if-statement.
    /// </summary>
    private static ExpressionSyntax? ExtractReturnExpression(IfStatementSyntax ifStmt)
    {
        if (ifStmt.Statement is ReturnStatementSyntax ret)
            return ret.Expression;

        if (ifStmt.Statement is BlockSyntax block)
        {
            var retStmt = block.Statements
                .OfType<ReturnStatementSyntax>()
                .FirstOrDefault();
            return retStmt?.Expression;
        }

        return null;
    }

    /// <summary>
    ///     Builds a bool-returning method that extracts the guard condition:
    ///     <c>private [static] bool Name(params) { return condition; }</c>
    /// </summary>
    private static MethodDeclarationSyntax BuildBoolGuardMethod(
        string name,
        List<ISymbol> parameters,
        ExpressionSyntax condition,
        bool isStatic,
        StatementSyntax anchorStmt)
    {
        var modifiers = $"private{(isStatic ? " static" : "")}";
        var paramStr = string.Join(", ", parameters.Select(p => $"{MethodExtractorBuilder.GetTypeName(p)} {p.Name}"));
        var memberIndent = MethodExtractorBuilder.MemberIndentOf(anchorStmt);
        var bodyIndent = MethodExtractorBuilder.IndentOf(anchorStmt);
        var condText = condition.ToFullString().Trim();
        var methodText =
            $"{modifiers} bool {name}({paramStr})\n{memberIndent}{{\n{bodyIndent}return {condText};\n{memberIndent}}}";

        var method = (MethodDeclarationSyntax)(SyntaxFactory.ParseMemberDeclaration(methodText)
                                               ?? throw new InvalidOperationException(
                                                   "Failed to parse the generated guard method."));

        return method.WithLeadingTrivia(
            SyntaxFactory.LineFeed,
            SyntaxFactory.Whitespace(memberIndent));
    }

    /// <summary>
    ///     Builds the call site for a guard extraction:
    ///     <c>if (NewMethod(args)) return value;</c>
    /// </summary>
    private static StatementSyntax BuildGuardCallSite(
        string methodName,
        List<ISymbol> parameters,
        ExpressionSyntax? returnExpr,
        StatementSyntax anchorStmt)
    {
        var argStr = string.Join(", ", parameters.Select(p => p.Name));
        string stmtText;
        if (returnExpr == null)
            stmtText = $"if ({methodName}({argStr})) return;";
        else
            stmtText = $"if ({methodName}({argStr})) return {returnExpr.ToFullString().Trim()};";

        return SyntaxFactory.ParseStatement(stmtText)
            .WithLeadingTrivia(anchorStmt.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.LineFeed);
    }

    /// <summary>
    ///     Locates the body statements of the newly-extracted method in the updated document
    ///     and returns the 1-based line range of those statements.
    /// </summary>
    private static async Task<(int startLine, int endLine)> FindNewMethodBodyRange(
        Solution solution,
        string filePath,
        string newMethodName)
    {
        var absPath = Path.GetFullPath(filePath);
        var doc = solution.Projects
                      .SelectMany(p => p.Documents)
                      .FirstOrDefault(d => string.Equals(d.FilePath, absPath, StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidOperationException($"File not found in solution after extraction: {absPath}");

        var root = await doc.GetSyntaxRootAsync()
                   ?? throw new InvalidOperationException("Failed to get syntax root after extraction.");

        var newMethodDecl = root.DescendantNodes()
                                .OfType<MethodDeclarationSyntax>()
                                .FirstOrDefault(m => m.Identifier.Text == newMethodName)
                            ?? throw new InvalidOperationException(
                                $"Could not locate newly-extracted method '{newMethodName}' in {absPath}.");

        var bodyStatements = newMethodDecl.Body?.Statements ?? [];
        // Filter out any trailing return statement that was generated (not part of the original body)
        var statements = bodyStatements
            .Where(s => s is not ReturnStatementSyntax)
            .ToList();

        if (statements.Count == 0)
            // Fall back to all statements if body only has a return
            statements = bodyStatements.ToList();

        if (statements.Count == 0)
            throw new InvalidOperationException($"Extracted method '{newMethodName}' has no body statements.");

        var firstSpan = statements[0].GetLocation().GetLineSpan();
        var lastSpan = statements[^1].GetLocation().GetLineSpan();

        return (firstSpan.StartLinePosition.Line + 1, lastSpan.EndLinePosition.Line + 1);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Steps 1–3: locate document, root, model, and validate selected statements
    // ────────────────────────────────────────────────────────────────────────────

    private static async Task<(Document doc, SyntaxNode root, SemanticModel model, List<StatementSyntax> selected)>
        FindDocumentAndStatements(Solution solution, string filePath, int startLine, int endLine)
    {
        // ── 1. Find document ──────────────────────────────────────────────────
        var absPath = Path.GetFullPath(filePath);
        var doc = solution.Projects
                      .SelectMany(p => p.Documents)
                      .FirstOrDefault(d => string.Equals(d.FilePath, absPath, StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidOperationException($"File not found in solution: {absPath}");

        // ── 2. Get syntax root and semantic model ─────────────────────────────
        var root = await doc.GetSyntaxRootAsync()
                   ?? throw new InvalidOperationException("Failed to get syntax root.");
        var model = await doc.GetSemanticModelAsync()
                    ?? throw new InvalidOperationException("Failed to get semantic model.");

        // ── 3. Find statements in line range (Roslyn lines are 0-based) ───────
        var selected = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(s => s.Parent is BlockSyntax)
            .Where(s =>
            {
                var span = s.GetLocation().GetLineSpan();
                return span.StartLinePosition.Line >= startLine - 1
                       && span.EndLinePosition.Line <= endLine - 1;
            })
            .ToList();

        if (selected.Count == 0)
            throw new InvalidOperationException(
                $"No statements found in lines {startLine}–{endLine}. " +
                "Verify the line range contains complete statements inside a method body.");

        // All selected statements must share the same block
        var distinctParents = selected.Select(s => s.Parent).Distinct().ToList();
        if (distinctParents.Count > 1)
            throw new InvalidOperationException(
                "Selected statements span multiple blocks. Extend or narrow the range to a single block.");

        // Check for control-flow patterns: guard patterns are handled above in ExtractAsync.
        // Reject other return patterns here (will be caught after Classify in ExtractAsync).
        var cfClass = ControlFlowUnwinder.Classify(selected);
        if (cfClass.Strategy != ControlFlowStrategy.ExtractAsBoolGuard
            && selected.Any(s => s.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any()))
            throw new InvalidOperationException(
                "Cannot extract statements that contain return statements.");

        return (doc, root, model, selected);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Step 4: data-flow analysis — classify modified outer locals as ref/out
    // ────────────────────────────────────────────────────────────────────────────

    private static DataFlowAnalysis ValidateDataFlow(SemanticModel model, List<StatementSyntax> selected)
    {
        var flow = model.AnalyzeDataFlow(selected.First(), selected.Last());
        if (flow == null || !flow.Succeeded)
            throw new InvalidOperationException("Roslyn data-flow analysis failed for the selected range.");

        // Variables declared inside the range that flow out are handled as return values.
        // Variables declared OUTSIDE the range that are written inside and read after are
        // ref/out parameters — no longer an error; classified in OrderParameters.
        return flow;
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Step 5: classify symbols into in-params, ref-params, out-params, return-vars
    //
    //  Four disjoint categories:
    //   orderedParams — DataFlowsIn symbols declared OUTSIDE the range that are
    //                   NOT also written inside (pure inputs).
    //   refParams     — declared OUTSIDE, appear in DataFlowsIn (read before write
    //                   inside) AND in DataFlowsOut (written inside, read after).
    //   outParams     — declared OUTSIDE, NOT in DataFlowsIn (never read before
    //                   write inside) BUT in DataFlowsOut (written inside, read after).
    //   returnVars    — declared INSIDE the range and flow out (tuple/single return).
    // ────────────────────────────────────────────────────────────────────────────

    private static (List<ISymbol> orderedParams, List<ISymbol> refParams, List<ISymbol> outParams, List<ISymbol>
        returnVars)
        OrderParameters(DataFlowAnalysis flow, List<StatementSyntax> selected, SemanticModel model)
    {
        // Symbols declared inside the selection
        var declaredInRange = selected
            .SelectMany(s => s.DescendantNodesAndSelf())
            .OfType<VariableDeclaratorSyntax>()
            .Select(v => model.GetDeclaredSymbol(v))
            .Where(s => s != null)
            .Select(s => s!)
            .ToHashSet(SymbolEqualityComparer.Default);

        // DataFlowsOut contains both:
        //   (a) vars declared inside that are used after — these become return values
        //   (b) vars declared outside that are written inside and read after — ref/out params
        var returnVars = flow.DataFlowsOut
            .Where(sym => declaredInRange.Contains(sym))
            .ToList();

        // Outer locals written inside and read after — must become ref or out params
        var outerWrittenOut = flow.DataFlowsOut
            .Where(sym => !declaredInRange.Contains(sym))
            .ToHashSet(SymbolEqualityComparer.Default);

        // ref: outer local that also flows IN (was read inside before being written)
        var refParams = outerWrittenOut
            .Where(sym => flow.DataFlowsIn.Contains(sym, SymbolEqualityComparer.Default))
            .ToList();

        // out: outer local that does NOT flow in (only written, not previously read inside)
        var outParams = outerWrittenOut
            .Where(sym => !flow.DataFlowsIn.Contains(sym, SymbolEqualityComparer.Default))
            .ToList();

        // Pure in-params: DataFlowsIn symbols that are NOT ref/out
        var refOrOutSet = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var s in refParams) refOrOutSet.Add(s);
        foreach (var s in outParams) refOrOutSet.Add(s);

        var seenParams = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var orderedParams = new List<ISymbol>();

        foreach (var ident in selected
                     .SelectMany(s => s.DescendantNodesAndSelf())
                     .OfType<IdentifierNameSyntax>()
                     .OrderBy(id => id.SpanStart))
        {
            var sym = model.GetSymbolInfo(ident).Symbol;
            if (sym == null) continue;
            if (refOrOutSet.Contains(sym)) continue; // handled as ref/out, not plain in
            var match = flow.DataFlowsIn.FirstOrDefault(p => SymbolEqualityComparer.Default.Equals(p, sym));
            if (match != null && seenParams.Add(match))
                orderedParams.Add(match);
        }

        // Order ref/out by first appearance in selected range
        refParams = OrderByFirstAppearance(refParams, selected, model);
        outParams = OrderByFirstAppearance(outParams, selected, model);

        return (orderedParams, refParams, outParams, returnVars);
    }

    /// <summary>Orders a set of symbols by their first identifier occurrence in the selected range.</summary>
    private static List<ISymbol> OrderByFirstAppearance(
        List<ISymbol> symbols, List<StatementSyntax> selected, SemanticModel model)
    {
        if (symbols.Count <= 1) return symbols;

        var symbolSet = new HashSet<ISymbol>(symbols, SymbolEqualityComparer.Default);
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var ordered = new List<ISymbol>();

        foreach (var ident in selected
                     .SelectMany(s => s.DescendantNodesAndSelf())
                     .OfType<IdentifierNameSyntax>()
                     .OrderBy(id => id.SpanStart))
        {
            var sym = model.GetSymbolInfo(ident).Symbol;
            if (sym == null) continue;
            var match = symbolSet.FirstOrDefault(s => SymbolEqualityComparer.Default.Equals(s, sym));
            if (match != null && seen.Add(match))
                ordered.Add(match);
        }

        // Append any not found via identifier walk (shouldn't happen, but defensive)
        foreach (var s in symbols)
            if (seen.Add(s))
                ordered.Add(s);

        return ordered;
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Step 6: find containing method, type, and static flag
    // ────────────────────────────────────────────────────────────────────────────

    private static (MethodDeclarationSyntax containingMethod, TypeDeclarationSyntax containingType, bool isStatic)
        FindContainingContext(List<StatementSyntax> selected)
    {
        var containingMethod = selected.First()
                                   .Ancestors()
                                   .OfType<MethodDeclarationSyntax>()
                                   .FirstOrDefault()
                               ?? throw new InvalidOperationException(
                                   "The selected statements are not inside a method declaration.");

        var containingType = containingMethod
                                 .Ancestors()
                                 .OfType<TypeDeclarationSyntax>()
                                 .FirstOrDefault()
                             ?? throw new InvalidOperationException(
                                 "The containing method is not inside a class or struct.");

        var isStatic = containingMethod.Modifiers.Any(SyntaxKind.StaticKeyword);

        return (containingMethod, containingType, isStatic);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Step 9: replace extracted statements with the call site in the block
    // ────────────────────────────────────────────────────────────────────────────

    private static MethodDeclarationSyntax SpliceCallSite(
        List<StatementSyntax> selected,
        StatementSyntax callSite,
        MethodDeclarationSyntax containingMethod)
    {
        var block = (BlockSyntax)selected.First().Parent!;
        var stmts = block.Statements;
        var firstIdx = stmts.IndexOf(selected.First());
        var lastIdx = stmts.IndexOf(selected.Last());

        for (var i = lastIdx; i >= firstIdx; i--)
            stmts = stmts.RemoveAt(i);

        stmts = stmts.Insert(firstIdx, callSite);

        var newBlock = block.WithStatements(stmts);
        return containingMethod.ReplaceNode(block, newBlock);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Step 10: replace containing method in type and insert the new method
    // ────────────────────────────────────────────────────────────────────────────

    private static TypeDeclarationSyntax InsertNewMethod(
        TypeDeclarationSyntax containingType,
        MethodDeclarationSyntax originalContainingMethod,
        MethodDeclarationSyntax updatedContainingMethod,
        MethodDeclarationSyntax newMethod,
        string? insertAfter,
        string? insertBefore)
    {
        var methodAnnotation = new SyntaxAnnotation();
        var annotatedNewContainingMethod = updatedContainingMethod.WithAdditionalAnnotations(methodAnnotation);
        var updatedType = containingType.ReplaceNode(originalContainingMethod, annotatedNewContainingMethod);

        MemberDeclarationSyntax anchorMember;
        bool afterAnchor;

        if (insertAfter != null || insertBefore != null)
        {
            var name = insertAfter ?? insertBefore!;
            anchorMember = updatedType.Members
                               .FirstOrDefault(m => GetMemberName(m) == name)
                           ?? throw new InvalidOperationException(
                               $"Member '{name}' not found in type '{containingType.Identifier.Text}'.");
            afterAnchor = insertAfter != null;
        }
        else
        {
            anchorMember = updatedType.Members
                               .FirstOrDefault(m => m.HasAnnotation(methodAnnotation))
                           ?? updatedType.Members.Last();
            afterAnchor = true;
        }

        var membersList = updatedType.Members.ToList();
        var anchorIdx = membersList.IndexOf(anchorMember);
        if (anchorIdx < 0)
            throw new InvalidOperationException("Internal error: anchor member not located after tree update.");

        var insertIdx = afterAnchor ? anchorIdx + 1 : anchorIdx;
        membersList.Insert(insertIdx, newMethod);

        return updatedType.WithMembers(SyntaxFactory.List(membersList));
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────────────

    private static string? GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text,
            _ => null
        };
    }
}
