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
        string? insertBefore = null)
    {
        if (startLine < 1) throw new ArgumentOutOfRangeException(nameof(startLine));
        if (endLine < 1) throw new ArgumentOutOfRangeException(nameof(endLine));
        if (startLine > endLine) throw new ArgumentOutOfRangeException(nameof(endLine));

        if (insertAfter != null && insertBefore != null)
            throw new InvalidOperationException("--after and --before are mutually exclusive.");

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

        // ── 4. DataFlow analysis ──────────────────────────────────────────────
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

        // ── 5. Sort DataFlowsIn by first reference position ───────────────────
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

        // ── 6. Find containing method and type ────────────────────────────────
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

        // ── 7. Build new method ───────────────────────────────────────────────
        var newMethod = BuildNewMethod(newMethodName, orderedParams, returnVars, selected, model, isStatic);

        // ── 8. Build call site ────────────────────────────────────────────────
        var callSite = BuildCallSite(newMethodName, orderedParams, returnVars, selected.First());

        // ── 9. Splice call site into the original block ───────────────────────
        var block = (BlockSyntax)selected.First().Parent!;
        var stmts = block.Statements;
        int firstIdx = stmts.IndexOf(selected.First());
        int lastIdx = stmts.IndexOf(selected.Last());

        for (int i = lastIdx; i >= firstIdx; i--)
            stmts = stmts.RemoveAt(i);

        stmts = stmts.Insert(firstIdx, callSite);

        var newBlock = block.WithStatements(stmts);
        var newContainingMethod = containingMethod.ReplaceNode(block, newBlock);

        // ── 10. Replace the containing method in the type, then insert the new method ──
        // Annotate the updated containing method so we can locate it by identity after ReplaceNode,
        // instead of relying on string/trivia comparisons that may differ across tree rewrites.
        var methodAnnotation = new SyntaxAnnotation();
        var annotatedNewContainingMethod = newContainingMethod.WithAdditionalAnnotations(methodAnnotation);
        var updatedType = containingType.ReplaceNode(containingMethod, annotatedNewContainingMethod);

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

        var finalType = updatedType.WithMembers(SyntaxFactory.List(membersList));
        var newRoot = root.ReplaceNode(containingType, finalType);

        return solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Method builder — text-based to guarantee correct trivia/spacing
    // ────────────────────────────────────────────────────────────────────────────

    private static MethodDeclarationSyntax BuildNewMethod(
        string name,
        IReadOnlyList<ISymbol> parameters,
        IReadOnlyList<ISymbol> returnVars,
        IReadOnlyList<StatementSyntax> bodyStatements,
        SemanticModel model,
        bool isStatic)
    {
        var modifiers = $"private{(isStatic ? " static" : "")}";
        var paramStr = string.Join(", ", parameters.Select(p => $"{GetTypeName(p)} {p.Name}"));

        string returnTypeStr;
        string returnStmtStr;

        if (returnVars.Count == 0)
        {
            returnTypeStr = "void";
            returnStmtStr = "";
        }
        else if (returnVars.Count == 1)
        {
            returnTypeStr = GetTypeName(returnVars[0]);
            returnStmtStr = "\n" + IndentOf(bodyStatements.Last()) + $"return {returnVars[0].Name};";
        }
        else
        {
            var tupleTypes = string.Join(", ", returnVars.Select(v => $"{GetTypeName(v)} {v.Name}"));
            returnTypeStr = $"({tupleTypes})";
            var tupleValues = string.Join(", ", returnVars.Select(v => v.Name));
            returnStmtStr = "\n" + IndentOf(bodyStatements.Last()) + $"return ({tupleValues});";
        }

        // Reconstruct body text from original statements to preserve indentation
        var bodyText = string.Join("", bodyStatements.Select(s => s.ToFullString()));

        // The method text is parsed at the file root level; leading indent is applied after parsing.
        var memberIndent = MemberIndentOf(bodyStatements.First());
        var methodText = $"{modifiers} {returnTypeStr} {name}({paramStr})\n{memberIndent}{{\n{bodyText}{returnStmtStr}\n{memberIndent}}}";

        var method = (MethodDeclarationSyntax)(SyntaxFactory.ParseMemberDeclaration(methodText)
            ?? throw new InvalidOperationException("Failed to parse the generated method."));

        // Add a blank line before the new method to separate it from the previous member
        return method.WithLeadingTrivia(
            SyntaxFactory.LineFeed,
            SyntaxFactory.Whitespace(memberIndent));
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Call site builder — text-based to guarantee correct trivia/spacing
    // ────────────────────────────────────────────────────────────────────────────

    private static StatementSyntax BuildCallSite(
        string methodName,
        IReadOnlyList<ISymbol> parameters,
        IReadOnlyList<ISymbol> returnVars,
        StatementSyntax firstExtracted)
    {
        var argStr = string.Join(", ", parameters.Select(p => p.Name));

        string stmtText;
        if (returnVars.Count == 0)
            stmtText = $"{methodName}({argStr});";
        else if (returnVars.Count == 1)
            stmtText = $"var {returnVars[0].Name} = {methodName}({argStr});";
        else
        {
            var varNames = string.Join(", ", returnVars.Select(v => v.Name));
            stmtText = $"var ({varNames}) = {methodName}({argStr});";
        }

        return SyntaxFactory.ParseStatement(stmtText)
            .WithLeadingTrivia(firstExtracted.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.LineFeed);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────────────

    private static string GetTypeName(ISymbol symbol)
    {
        var type = symbol switch
        {
            ILocalSymbol loc => loc.Type,
            IParameterSymbol param => param.Type,
            _ => throw new InvalidOperationException(
                $"Unexpected symbol kind when resolving type: {symbol.Kind} ({symbol.Name})")
        };
        return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static string? GetMemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        _ => null
    };

    /// <summary>Returns the leading whitespace string of a statement (body-level indent).</summary>
    private static string IndentOf(StatementSyntax stmt)
    {
        var ws = stmt.GetLeadingTrivia()
            .LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        return ws == default ? "        " : ws.ToString();
    }

    /// <summary>Returns the indent string one level up from body indent (member/class-body level).</summary>
    private static string MemberIndentOf(StatementSyntax stmt)
    {
        var bodyIndent = IndentOf(stmt);
        // Trim one 4-space level to get the member indent (class body level)
        return bodyIndent.Length >= 4
            ? bodyIndent[..^4]
            : bodyIndent;
    }
}
