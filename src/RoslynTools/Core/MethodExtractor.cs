using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal static class MethodExtractor
{
    /// <summary>
    /// Extracts statements within <paramref name="startLine"/>–<paramref name="endLine"/> (1-based, inclusive)
    /// from the method that contains them, placing a new private method named <paramref name="newMethodName"/>
    /// at the specified position within the same type.
    /// </summary>
    internal static async Task<Solution> ExtractAsync(
        Solution solution,
        string filePath,
        string newMethodName,
        int startLine,
        int endLine,
        string? insertAfter = null,
        string? insertBefore = null,
        bool replaceDuplicates = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startLine, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(endLine, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startLine, endLine, nameof(startLine));

        if (insertAfter != null && insertBefore != null)
            throw new InvalidOperationException("--after and --before are mutually exclusive.");

        var (doc, root, model, selected) = await FindDocumentAndStatements(solution, filePath, startLine, endLine);
        var flow = ValidateDataFlow(model, selected);
        var (orderedParams, returnVars) = OrderParameters(flow, selected, model);
        var (containingMethod, containingType, isStatic) = FindContainingContext(selected);

        // ── 7. Build new method ───────────────────────────────────────────────
        var newMethod = MethodExtractorBuilder.BuildNewMethod(newMethodName, orderedParams, returnVars, selected, model, isStatic);

        // ── 8. Build call site ────────────────────────────────────────────────
        var callSite = MethodExtractorBuilder.BuildCallSite(newMethodName, orderedParams, returnVars, selected.First());

        // ── 9. Splice call site into the original block ───────────────────────
        var newContainingMethod = SpliceCallSite(selected, callSite, containingMethod);

        // ── 10. Replace the containing method in the type, then insert the new method ──
        var finalType = InsertNewMethod(containingType, containingMethod, newContainingMethod, newMethod, insertAfter, insertBefore);
        var newRoot = root.ReplaceNode(containingType, finalType);

        var extractedSln = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);

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
            returnVars);
    }

    /// <summary>
    /// Locates the body statements of the newly-extracted method in the updated document
    /// and returns the 1-based line range of those statements.
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
            ?? throw new InvalidOperationException($"Could not locate newly-extracted method '{newMethodName}' in {absPath}.");

        var bodyStatements = newMethodDecl.Body?.Statements ?? [];
        // Filter out any trailing return statement that was generated (not part of the original body)
        var statements = bodyStatements
            .Where(s => s is not ReturnStatementSyntax)
            .ToList();

        if (statements.Count == 0)
        {
            // Fall back to all statements if body only has a return
            statements = bodyStatements.ToList();
        }

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

        // Bail on embedded returns — the semantics are too complex for a first pass
        if (selected.Any(s => s.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any()))
            throw new InvalidOperationException(
                "Cannot extract statements that contain return statements.");

        return (doc, root, model, selected);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Step 4: data-flow analysis — reject variables that would need out-params
    // ────────────────────────────────────────────────────────────────────────────

    private static DataFlowAnalysis ValidateDataFlow(SemanticModel model, List<StatementSyntax> selected)
    {
        var flow = model.AnalyzeDataFlow(selected.First(), selected.Last());
        if (flow == null || !flow.Succeeded)
            throw new InvalidOperationException("Roslyn data-flow analysis failed for the selected range.");

        // Compute which symbols are declared within the extracted range (syntax-level check,
        // avoids relying on DataFlowAnalysis.VariablesDeclaredWithin which is absent in some builds).
        var declaredInRange = selected
            .SelectMany(s => s.DescendantNodesAndSelf())
            .OfType<VariableDeclaratorSyntax>()
            .Select(v => (ISymbol?)model.GetDeclaredSymbol(v))
            .Where(s => s != null)
            .Select(s => s!)
            .ToHashSet(SymbolEqualityComparer.Default);

        // Variables declared outside the range but written inside and read after are
        // not safely extractable without out-parameters — reject and guide the user.
        var unsafeOut = flow.DataFlowsOut
            .Where(sym => !declaredInRange.Contains(sym))
            .ToList();
        if (unsafeOut.Count > 0)
        {
            var names = string.Join(", ", unsafeOut.Select(s => s.Name));
            throw new InvalidOperationException(
                $"Cannot extract: variable(s) '{names}' are declared before the selection but " +
                "written inside and read after. Extend the selection to include their declarations.");
        }

        return flow;
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Step 5: sort DataFlowsIn by first reference position
    // ────────────────────────────────────────────────────────────────────────────

    private static (List<ISymbol> orderedParams, List<ISymbol> returnVars)
        OrderParameters(DataFlowAnalysis flow, List<StatementSyntax> selected, SemanticModel model)
    {
        var seenParams = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var orderedParams = new List<ISymbol>();

        foreach (var ident in selected
            .SelectMany(s => s.DescendantNodesAndSelf())
            .OfType<IdentifierNameSyntax>()
            .OrderBy(id => id.SpanStart))
        {
            var sym = model.GetSymbolInfo(ident).Symbol;
            if (sym == null) continue;
            var match = flow.DataFlowsIn.FirstOrDefault(
                p => SymbolEqualityComparer.Default.Equals(p, sym));
            if (match != null && seenParams.Add(match))
                orderedParams.Add(match);
        }

        var returnVars = flow.DataFlowsOut.ToList();
        return (orderedParams, returnVars);
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

        bool isStatic = containingMethod.Modifiers.Any(SyntaxKind.StaticKeyword);

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
        int firstIdx = stmts.IndexOf(selected.First());
        int lastIdx = stmts.IndexOf(selected.Last());

        for (int i = lastIdx; i >= firstIdx; i--)
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
        // Annotate the updated containing method so we can locate it by identity after ReplaceNode,
        // instead of relying on string/trivia comparisons that may differ across tree rewrites.
        var methodAnnotation = new SyntaxAnnotation();
        var annotatedNewContainingMethod = updatedContainingMethod.WithAdditionalAnnotations(methodAnnotation);
        var updatedType = containingType.ReplaceNode(originalContainingMethod, annotatedNewContainingMethod);

        // Locate the anchor member in the updated type for placement
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
            // Default: after the (updated) containing method — found via annotation to avoid trivia mismatches
            anchorMember = updatedType.Members
                .FirstOrDefault(m => m.HasAnnotation(methodAnnotation))
                ?? updatedType.Members.Last();
            afterAnchor = true;
        }

        var membersList = updatedType.Members.ToList();
        int anchorIdx = membersList.IndexOf(anchorMember);
        if (anchorIdx < 0)
            throw new InvalidOperationException("Internal error: anchor member not located after tree update.");

        int insertIdx = afterAnchor ? anchorIdx + 1 : anchorIdx;
        membersList.Insert(insertIdx, newMethod);

        return updatedType.WithMembers(SyntaxFactory.List(membersList));
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────────────

    private static string? GetMemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        _ => null
    };
}
