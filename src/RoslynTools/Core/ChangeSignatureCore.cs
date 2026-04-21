using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace RoslynTools.Core;

internal static class ChangeSignatureCore
{
    internal static async Task<Solution> RenameParamAsync(
        Solution solution,
        string methodSpec,
        string oldName,
        string newName)
    {
        var methodSymbol = await SymbolLocator.FindMethodBySpecAsync(solution, methodSpec);

        var parameter = methodSymbol.Parameters.FirstOrDefault(param => param.Name == oldName)
            ?? throw new InvalidOperationException(
                $"Parameter '{oldName}' was not found on '{methodSpec}'.");

        if (methodSymbol.Parameters.Any(param => param.Name == newName))
        {
            throw new InvalidOperationException(
                $"Method '{methodSpec}' already has a parameter named '{newName}'.");
        }

        var options = new SymbolRenameOptions(
            RenameOverloads: false,
            RenameInStrings: false,
            RenameInComments: false,
            RenameFile: false);

        return await Renamer.RenameSymbolAsync(solution, parameter, options, newName);
    }

    /// <summary>
    /// Reorders parameters of a method and updates all call sites.
    /// </summary>
    /// <param name="solution">The solution to update.</param>
    /// <param name="methodSpec">FQN of the method.</param>
    /// <param name="newOrder">
    /// Array of zero-based index strings (e.g. ["2","0","1"]) OR parameter name strings
    /// (e.g. ["c","a","b"]). Mixed mode is rejected. Length must equal parameter count.
    /// </param>
    internal static async Task<Solution> ReorderParamsAsync(
        Solution solution,
        string methodSpec,
        string[] newOrder)
    {
        var methodSymbol = await SymbolLocator.FindMethodBySpecAsync(solution, methodSpec);
        var parameters = methodSymbol.Parameters;
        int paramCount = parameters.Length;

        if (newOrder.Length != paramCount)
            throw new InvalidOperationException(
                $"Order list length {newOrder.Length} does not match parameter count {paramCount}.");

        if (parameters.Any(p => p.IsParams))
            throw new InvalidOperationException(
                $"Reordering is not supported for methods with a 'params' array parameter (v1 limitation).");

        // Determine if the order is numeric or name-based (mixed is rejected).
        bool[] isNumeric = newOrder.Select(s => int.TryParse(s, out _)).ToArray();
        if (isNumeric.Any(b => b) && !isNumeric.All(b => b))
            throw new InvalidOperationException(
                "mixed numeric index and parameter name entries in the order list are not supported. " +
                "Use all indices or all names.");

        int[] indices;
        if (isNumeric.All(b => b))
        {
            indices = newOrder.Select(int.Parse).ToArray();

            var outOfRange = indices.FirstOrDefault(i => i < 0 || i >= paramCount, -1);
            if (outOfRange != -1 && !indices.All(i => i >= 0 && i < paramCount))
                throw new InvalidOperationException(
                    $"Index {indices.First(i => i < 0 || i >= paramCount)} is out of range for a method with {paramCount} parameter(s).");

            // Check each individually
            foreach (var idx in indices)
            {
                if (idx < 0 || idx >= paramCount)
                    throw new InvalidOperationException(
                        $"Index {idx} is out of range for a method with {paramCount} parameter(s).");
            }
        }
        else
        {
            // Name-based
            var nameToIndex = parameters
                .Select((p, i) => (p.Name, i))
                .ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);

            indices = new int[newOrder.Length];
            for (int i = 0; i < newOrder.Length; i++)
            {
                if (!nameToIndex.TryGetValue(newOrder[i], out int idx))
                    throw new InvalidOperationException(
                        $"Parameter name '{newOrder[i]}' does not match any parameter of '{methodSpec}'.");
                indices[i] = idx;
            }
        }

        // Validate: no duplicates
        if (indices.Distinct().Count() != indices.Length)
            throw new InvalidOperationException(
                "The order list contains duplicate entries.");

        // Identity check — return unchanged solution
        if (indices.Select((idx, pos) => idx == pos).All(b => b))
            return solution;

        // Apply changes: rewrite declaration(s) and all call sites
        return await ApplyReorderAsync(solution, methodSymbol, indices);
    }

    internal static async Task<Solution> AddParamAsync(
        Solution solution,
        string methodSpec,
        string paramSpec,
        string? afterParam)
    {
        var methodSymbol = await SymbolLocator.FindMethodBySpecAsync(solution, methodSpec);

        // Parse the parameter spec using SyntaxFactory
        var parsedList = SyntaxFactory.ParseParameterList($"({paramSpec})");
        if (parsedList.Parameters.Count != 1)
        {
            throw new InvalidOperationException(
                $"Could not parse '{paramSpec}' as a single parameter declaration.");
        }

        var newParam = parsedList.Parameters[0];

        // Require a default value so existing call sites remain valid
        if (newParam.Default is null)
        {
            throw new InvalidOperationException(
                "A default value is required for add-param (the new parameter must not break existing call sites).");
        }

        // Check for name conflict
        var newParamName = newParam.Identifier.Text;
        if (methodSymbol.Parameters.Any(p => p.Name == newParamName))
        {
            throw new InvalidOperationException(
                $"Method '{methodSpec}' already has a parameter named '{newParamName}'.");
        }

        // Determine insertion index
        int insertAfterIndex;
        if (afterParam is null)
        {
            insertAfterIndex = methodSymbol.Parameters.Length - 1; // append at end
        }
        else
        {
            var afterIndex = -1;
            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                if (methodSymbol.Parameters[i].Name == afterParam)
                {
                    afterIndex = i;
                    break;
                }
            }

            if (afterIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Parameter '{afterParam}' was not found on '{methodSpec}'.");
            }

            insertAfterIndex = afterIndex;
        }

        // Find and rewrite declaration(s) — a method may have multiple partial declarations
        var currentSolution = solution;

        foreach (var reference in methodSymbol.DeclaringSyntaxReferences)
        {
            var syntaxNode = await reference.GetSyntaxAsync();
            var document = currentSolution.GetDocument(syntaxNode.SyntaxTree);
            if (document is null)
                continue;

            var sourceText = await document.GetTextAsync();

            // Find the parameter list for this declaration
            ParameterListSyntax? paramList = syntaxNode switch
            {
                MethodDeclarationSyntax m => m.ParameterList,
                ConstructorDeclarationSyntax c => c.ParameterList,
                LocalFunctionStatementSyntax lf => lf.ParameterList,
                _ => null
            };

            if (paramList is null)
                continue;

            var existingParams = paramList.Parameters;
            if (existingParams.Count == 0)
            {
                // Empty param list — insert at position right after '('
                var insertPos = paramList.OpenParenToken.Span.End;
                var paramText = newParam.ToFullString().Trim();
                var changes = new[] { new TextChange(new TextSpan(insertPos, 0), paramText) };
                currentSolution = currentSolution.WithDocumentText(
                    document.Id,
                    sourceText.WithChanges(changes));
            }
            else
            {
                // Insert after the target param using a zero-length TextChange
                var targetParam = existingParams[insertAfterIndex];
                var insertPosition = targetParam.Span.End;
                var paramText = newParam.ToFullString().Trim();
                var insertion = $", {paramText}";

                var changes = new[] { new TextChange(new TextSpan(insertPosition, 0), insertion) };
                currentSolution = currentSolution.WithDocumentText(
                    document.Id,
                    sourceText.WithChanges(changes));
            }
        }

        return currentSolution;
    }

    internal static async Task<Solution> RemoveParamAsync(
        Solution solution,
        string methodSpec,
        string paramName,
        bool force)
    {
        var methodSymbol = await SymbolLocator.FindMethodBySpecAsync(solution, methodSpec);

        // Validate parameter exists
        var paramIndex = -1;
        for (int i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            if (methodSymbol.Parameters[i].Name == paramName)
            {
                paramIndex = i;
                break;
            }
        }

        if (paramIndex < 0)
        {
            throw new InvalidOperationException(
                $"Parameter '{paramName}' was not found on '{methodSpec}'.");
        }

        var param = methodSymbol.Parameters[paramIndex];

        // Require default value unless --force
        if (!force && !param.HasExplicitDefaultValue)
        {
            throw new InvalidOperationException(
                $"Parameter '{paramName}' has no default value. Use --force to remove it anyway.");
        }

        // Collect all changes against the original solution, grouped by document,
        // then apply them in one pass per document to avoid offset drift.
        var changesByDoc = new Dictionary<DocumentId, List<TextChange>>();

        // Step 1: Collect declaration changes
        foreach (var declRef in methodSymbol.DeclaringSyntaxReferences)
        {
            var syntaxNode = await declRef.GetSyntaxAsync();
            var document = solution.GetDocument(syntaxNode.SyntaxTree);
            if (document is null)
                continue;

            var sourceText = await document.GetTextAsync();

            ParameterListSyntax? paramList = syntaxNode switch
            {
                MethodDeclarationSyntax m => m.ParameterList,
                ConstructorDeclarationSyntax c => c.ParameterList,
                LocalFunctionStatementSyntax lf => lf.ParameterList,
                _ => null
            };

            if (paramList is null)
                continue;

            var changes = BuildParameterRemovalChanges(paramList, paramIndex, sourceText);
            if (changes.Length > 0)
            {
                if (!changesByDoc.TryGetValue(document.Id, out var docChanges))
                    changesByDoc[document.Id] = docChanges = [];
                docChanges.AddRange(changes);
            }
        }

        // Step 2: Collect call-site changes using the original solution
        var symbolRefs = await SymbolFinder.FindReferencesAsync(methodSymbol, solution);

        var docGroups = symbolRefs
            .SelectMany(r => r.Locations)
            .Where(loc => !loc.IsImplicit)
            .GroupBy(loc => loc.Document.Id);

        foreach (var group in docGroups)
        {
            var docId = group.Key;
            var document = solution.GetDocument(docId);
            if (document is null)
                continue;

            var sourceText = await document.GetTextAsync();
            var root = await document.GetSyntaxRootAsync();
            if (root is null)
                continue;

            if (!changesByDoc.TryGetValue(docId, out var docChanges))
                changesByDoc[docId] = docChanges = [];

            foreach (var loc in group)
            {
                var node = root.FindNode(loc.Location.SourceSpan);
                var argList = FindArgumentList(node);
                if (argList is null)
                {
                    // Not an invocation — likely a method group, nameof, or attribute.
                    // The signature change may break this site; warn so the user can inspect.
                    Console.Error.WriteLine(
                        $"  Warning: reference at {loc.Location} is not an invocation and was not rewritten automatically. Inspect manually.");
                    continue;
                }

                // Extension methods called in instance style (receiver.Foo(args)) exclude
                // the receiver from the arg list, so the positional index must be adjusted.
                var callSiteParamIndex = (methodSymbol.IsExtensionMethod
                    && argList.Arguments.Count < methodSymbol.Parameters.Length)
                    ? paramIndex - 1
                    : paramIndex;

                if (callSiteParamIndex < 0)
                    continue; // removing the implicit receiver param — nothing to rewrite

                var argChanges = BuildArgumentRemovalChanges(argList, callSiteParamIndex, paramName, sourceText);
                docChanges.AddRange(argChanges);
            }
        }

        // Step 3: Apply all changes per document
        var currentSolution = solution;
        foreach (var (docId, docChanges) in changesByDoc)
        {
            var document = solution.GetDocument(docId);
            if (document is null)
                continue;

            var sourceText = await document.GetTextAsync();
            var ordered = docChanges
                .OrderBy(c => c.Span.Start)
                .ToList();

            currentSolution = currentSolution.WithDocumentText(
                docId,
                sourceText.WithChanges(ordered));
        }

        return currentSolution;
    }

    private static async Task<Solution> ApplyReorderAsync(
        Solution solution,
        IMethodSymbol methodSymbol,
        int[] newParamOrder)
    {
        // Collect all locations to rewrite: declarations + invocations
        var references = await SymbolFinder.FindReferencesAsync(methodSymbol, solution);

        // Track which documents need edits (document id -> list of text replacements)
        // We'll do one pass per document, collecting all spans to replace.
        var edits = new Dictionary<DocumentId, List<(TextSpan Span, string NewText)>>();

        void AddEdit(DocumentId docId, TextSpan span, string newText)
        {
            if (!edits.TryGetValue(docId, out var list))
                edits[docId] = list = new List<(TextSpan, string)>();
            list.Add((span, newText));
        }

        // Find all declaration syntax nodes for this method across the solution
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                var semanticModel = await document.GetSemanticModelAsync();
                if (root is null || semanticModel is null)
                    continue;

                foreach (var node in root.DescendantNodes())
                {
                    ParameterListSyntax? paramList = node switch
                    {
                        MethodDeclarationSyntax m => m.ParameterList,
                        ConstructorDeclarationSyntax c => c.ParameterList,
                        LocalFunctionStatementSyntax lf => lf.ParameterList,
                        _ => null
                    };

                    if (paramList is null)
                        continue;

                    var declared = semanticModel.GetDeclaredSymbol(node) as IMethodSymbol;
                    if (declared is null)
                        continue;

                    if (!SymbolEqualityComparer.Default.Equals(declared, methodSymbol)
                        && !SymbolEqualityComparer.Default.Equals(declared.OriginalDefinition, methodSymbol.OriginalDefinition))
                        continue;

                    // Reorder the parameter list
                    var oldParams = paramList.Parameters;
                    if (oldParams.Count != newParamOrder.Length)
                        continue;

                    var reordered = newParamOrder.Select(i => oldParams[i]).ToArray();

                    // Rebuild parameter list preserving separators style
                    var newParams = SyntaxFactory.SeparatedList(
                        reordered,
                        Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space), reordered.Length - 1));

                    var newParamList = paramList.WithParameters(newParams);
                    AddEdit(document.Id, paramList.Span, newParamList.ToFullString());
                }
            }
        }

        // Process call sites
        int paramCount = methodSymbol.Parameters.Length;
        var paramNames = methodSymbol.Parameters.Select(p => p.Name).ToArray();

        // Build reverse map: old-position -> new-position
        // newParamOrder[newPos] = oldPos => reverseMap[oldPos] = newPos
        var reverseMap = new int[paramCount];
        for (int newPos = 0; newPos < paramCount; newPos++)
            reverseMap[newParamOrder[newPos]] = newPos;

        foreach (var referenceGroup in references)
        {
            foreach (var location in referenceGroup.Locations)
            {
                if (location.Location.SourceTree is null)
                    continue;

                var document = solution.GetDocument(location.Location.SourceTree);
                if (document is null)
                    continue;

                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var node = root.FindNode(location.Location.SourceSpan);

                // Walk up to find the invocation or object creation
                var invocationOrCreation = node.AncestorsAndSelf()
                    .FirstOrDefault(n => n is InvocationExpressionSyntax or ObjectCreationExpressionSyntax);

                if (invocationOrCreation is null)
                    continue;

                ArgumentListSyntax? argList = invocationOrCreation switch
                {
                    InvocationExpressionSyntax inv => inv.ArgumentList,
                    ObjectCreationExpressionSyntax oc => oc.ArgumentList,
                    _ => null
                };

                if (argList is null)
                    continue;

                var args = argList.Arguments;
                if (args.Count == 0)
                    continue;

                if (args.Count < paramCount)
                {
                    // Trailing args with default values may be legitimately omitted.
                    // Throw only if any omitted param is required, or if any omitted
                    // param would move to a non-trailing position after reorder.
                    bool allOmittedHaveDefaults = methodSymbol.Parameters
                        .Skip(args.Count)
                        .All(p => p.HasExplicitDefaultValue);
                    if (!allOmittedHaveDefaults)
                        throw new InvalidOperationException(
                            $"Call site at {location.Location} is missing required arguments, which is not supported in v1.");
                    var omittedPositions = Enumerable.Range(args.Count, paramCount - args.Count).ToHashSet();
                    if (omittedPositions.Any(oldPos => reverseMap[oldPos] < args.Count))
                        throw new InvalidOperationException(
                            $"Call site at {location.Location} omits optional argument(s) that would move to a non-trailing position after reorder. Specify all arguments explicitly first.");
                    continue;
                }

                var newArgs = ReorderArguments(args, paramNames, reverseMap);

                // Rebuild argument list
                var newArgList = argList.WithArguments(
                    SyntaxFactory.SeparatedList(
                        newArgs,
                        Enumerable.Repeat(
                            SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
                            newArgs.Length - 1)));

                AddEdit(document.Id, argList.Span, newArgList.ToFullString());
            }
        }

        // Apply all edits per document — spans are stable relative to the original text,
        // so SourceText.WithChanges can apply them all at once without offset drift.
        foreach (var (docId, replacements) in edits)
        {
            var document = solution.GetDocument(docId)!;
            var sourceText = await document.GetTextAsync();
            var changes = replacements
                .OrderBy(r => r.Span.Start)
                .Select(r => new TextChange(r.Span, r.NewText));
            solution = solution.WithDocumentText(docId, sourceText.WithChanges(changes));
        }

        return solution;
    }

    private static ArgumentSyntax[] ReorderArguments(
        SeparatedSyntaxList<ArgumentSyntax> args,
        string[] paramNames,
        int[] reverseMap)
    {
        int paramCount = paramNames.Length;

        // Split into positional prefix and named tail
        int positionalCount = 0;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].NameColon is null)
                positionalCount++;
            else
                break;
        }

        // Map each argument to which parameter it belongs
        // positional args[i] maps to parameter i
        // named args map to their named parameter
        var argByOldParamPos = new ArgumentSyntax?[paramCount];

        for (int i = 0; i < positionalCount; i++)
            argByOldParamPos[i] = args[i];

        for (int i = positionalCount; i < args.Count; i++)
        {
            var arg = args[i];
            var name = arg.NameColon!.Name.Identifier.Text;
            int paramIdx = Array.IndexOf(paramNames, name);
            argByOldParamPos[paramIdx] = arg;
        }

        // Build newParamOrder from reverseMap
        var newParamOrder = new int[paramCount];
        for (int oldPos = 0; oldPos < paramCount; oldPos++)
            newParamOrder[reverseMap[oldPos]] = oldPos;

        // Determine the new positional count.
        // C# requires: all positional args precede all named args.
        // Rule: find the last originally-positional parameter's new position; everything
        // up to and including that new position must be positional (to keep positional
        // args contiguous at the start).
        int newPositionalCount = 0;
        for (int oldPos = 0; oldPos < positionalCount; oldPos++)
        {
            int newPos = reverseMap[oldPos];
            if (newPos + 1 > newPositionalCount)
                newPositionalCount = newPos + 1;
        }

        var result = new ArgumentSyntax[paramCount];
        for (int newPos = 0; newPos < paramCount; newPos++)
        {
            int oldPos = newParamOrder[newPos];
            var origArg = argByOldParamPos[oldPos]!;

            if (newPos < newPositionalCount)
            {
                // Strip NameColon if present (shouldn't be for original positionals)
                result[newPos] = origArg.WithNameColon(null);
            }
            else
            {
                // Ensure NameColon is present
                if (origArg.NameColon is not null)
                {
                    result[newPos] = origArg;
                }
                else
                {
                    var nameColonSyntax = SyntaxFactory.NameColon(
                        SyntaxFactory.IdentifierName(paramNames[oldPos]));
                    result[newPos] = origArg.WithNameColon(nameColonSyntax);
                }
            }
        }

        return result;
    }

    private static TextChange[] BuildParameterRemovalChanges(
        ParameterListSyntax paramList,
        int paramIndex,
        SourceText sourceText)
    {
        var parameters = paramList.Parameters;
        if (paramIndex < 0 || paramIndex >= parameters.Count)
            return [];

        var param = parameters[paramIndex];

        // Determine the span to remove including surrounding separator
        TextSpan removeSpan;
        if (parameters.Count == 1)
        {
            // Only parameter — remove just the parameter content, leave parens
            removeSpan = param.Span;
        }
        else if (paramIndex == parameters.Count - 1)
        {
            // Last parameter — remove the comma before it
            var separatorSpan = paramList.Parameters.GetSeparators().ElementAt(paramIndex - 1).Span;
            removeSpan = TextSpan.FromBounds(separatorSpan.Start, param.Span.End);
        }
        else
        {
            // Not last — remove the param and the comma+space after it
            var separatorSpan = paramList.Parameters.GetSeparators().ElementAt(paramIndex).Span;
            var endPos = separatorSpan.End;
            // Consume one trailing space if present
            if (endPos < sourceText.Length && sourceText[endPos] == ' ')
                endPos++;
            removeSpan = TextSpan.FromBounds(param.Span.Start, endPos);
        }

        return [new TextChange(removeSpan, "")];
    }

    private static ArgumentListSyntax? FindArgumentList(SyntaxNode node)
    {
        // Reference locations typically point to the method name identifier.
        // Walk up to find the invocation expression or object creation expression.
        var current = node;
        while (current is not null)
        {
            if (current is InvocationExpressionSyntax invocation)
                return invocation.ArgumentList;
            if (current is ObjectCreationExpressionSyntax objectCreation)
                return objectCreation.ArgumentList;
            if (current is ImplicitObjectCreationExpressionSyntax implicitCreation)
                return implicitCreation.ArgumentList;
            // Stop at statement boundaries to avoid false matches
            if (current is StatementSyntax or MemberDeclarationSyntax or CompilationUnitSyntax)
                break;
            current = current.Parent;
        }

        return null;
    }

    private static TextChange[] BuildArgumentRemovalChanges(
        ArgumentListSyntax argList,
        int paramIndex,
        string paramName,
        SourceText sourceText)
    {
        var args = argList.Arguments;

        // Check if this call site uses named arguments for the target param
        var namedArg = args.FirstOrDefault(a =>
            a.NameColon?.Name.Identifier.Text == paramName);

        if (namedArg is not null)
        {
            // Remove the named argument
            return BuildArgRemovalChanges(args, args.IndexOf(namedArg), sourceText);
        }

        // Positional: only remove if we have enough positional args
        if (paramIndex >= args.Count)
            return []; // arg was already using default — nothing to remove

        // Ensure the argument at this index is positional (no name colon)
        var positionalArg = args[paramIndex];
        if (positionalArg.NameColon is not null)
        {
            // It's named but not for our param — skip
            return [];
        }

        return BuildArgRemovalChanges(args, paramIndex, sourceText);
    }

    private static TextChange[] BuildArgRemovalChanges(
        SeparatedSyntaxList<ArgumentSyntax> args,
        int argIndex,
        SourceText sourceText)
    {
        if (argIndex < 0 || argIndex >= args.Count)
            return [];

        var arg = args[argIndex];
        TextSpan removeSpan;

        if (args.Count == 1)
        {
            removeSpan = arg.Span;
        }
        else if (argIndex == args.Count - 1)
        {
            // Last argument — remove comma before it
            var separatorSpan = args.GetSeparators().ElementAt(argIndex - 1).Span;
            removeSpan = TextSpan.FromBounds(separatorSpan.Start, arg.Span.End);
        }
        else
        {
            // Not last — remove arg + following comma + optional space
            var separatorSpan = args.GetSeparators().ElementAt(argIndex).Span;
            var endPos = separatorSpan.End;
            if (endPos < sourceText.Length && sourceText[endPos] == ' ')
                endPos++;
            removeSpan = TextSpan.FromBounds(arg.Span.Start, endPos);
        }

        return [new TextChange(removeSpan, "")];
    }
}
