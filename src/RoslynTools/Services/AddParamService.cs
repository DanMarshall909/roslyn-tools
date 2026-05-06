using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class AddParamService
{
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

            var paramList = ParameterChangeHelper.GetParameterList(syntaxNode);
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
}
