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
                // Build list of TextChanges against the original source
                // Insert after position insertAfterIndex within the parameter list

                // We want to insert ", newParamText" after the param at insertAfterIndex
                // Or if insertAfterIndex == -1 (empty was handled above), insert at start

                var targetParam = existingParams[insertAfterIndex];
                var insertPosition = targetParam.Span.End;

                // Format: ", newParam" — using the separator from the existing list if available
                var paramText = newParam.ToFullString().Trim();
                var insertion = $", {paramText}";

                var changes = new[] { new TextChange(new TextSpan(insertPosition, 0), insertion) };
                var newText = sourceText.WithChanges(changes);

                currentSolution = currentSolution.WithDocumentText(
                    document.Id,
                    newText);
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
        if (!force && param.HasExplicitDefaultValue is false)
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
            // Remove from start of param to end of separator, plus any trailing space
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
