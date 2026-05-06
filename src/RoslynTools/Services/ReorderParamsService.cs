using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class ReorderParamsService : IRtService
{
    private readonly ISymbolResolutionService _symbols;
    private readonly ParameterChangeHelper _parameterChangeHelper;

    public ReorderParamsService()
        : this(new ParameterChangeHelper())
    {
    }

    public ReorderParamsService(ParameterChangeHelper parameterChangeHelper,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();

        _parameterChangeHelper = parameterChangeHelper;
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
    internal async Task<Solution> ReorderParamsAsync(
        Solution solution,
        string methodSpec,
        string[] newOrder)
    {
        var methodSymbol = await _symbols.FindMethodBySpecAsync(solution, methodSpec);
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

    private async Task<Solution> ApplyReorderAsync(
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
                    var paramList = _parameterChangeHelper.GetParameterList(node);
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

    private ArgumentSyntax[] ReorderArguments(
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
}
