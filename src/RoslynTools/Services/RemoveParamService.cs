using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Removes parameters from code by identifying and modifying relevant elements.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class RemoveParamService : IRtService
{
    private readonly ILogger<RemoveParamService> _logger;
    private readonly ParameterChangeHelper _parameterChangeHelper;
    private readonly ISymbolResolutionService _symbols;

    public RemoveParamService()
        : this(new RtConsoleLogger<RemoveParamService>(), new ParameterChangeHelper())
    {
    }

    public RemoveParamService(ILogger<RemoveParamService> logger, ParameterChangeHelper parameterChangeHelper,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();

        _logger = logger;
        _parameterChangeHelper = parameterChangeHelper;
    }

    /// <summary>
    /// Asynchronously removes a parameter from code within a solution.
    /// </summary>
    internal async Task<Solution> RemoveParamAsync(
        Solution solution,
        string methodSpec,
        string paramName,
        bool force)
    {
        var methodSymbol = await _symbols.FindMethodBySpecAsync(solution, methodSpec);

        // Validate parameter exists
        var paramIndex = -1;
        for (var i = 0; i < methodSymbol.Parameters.Length; i++)
            if (methodSymbol.Parameters[i].Name == paramName)
            {
                paramIndex = i;
                break;
            }

        if (paramIndex < 0)
            throw new InvalidOperationException(
                $"Parameter '{paramName}' was not found on '{methodSpec}'.");

        var param = methodSymbol.Parameters[paramIndex];

        // Require default value unless --force
        if (!force && !param.HasExplicitDefaultValue)
            throw new InvalidOperationException(
                $"Parameter '{paramName}' has no default value. Use --force to remove it anyway.");

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

            var paramList = _parameterChangeHelper.GetParameterList(syntaxNode);
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
                    _logger.RtWarning(
                        "Reference at {Location} is not an invocation and was not rewritten automatically. Inspect manually.",
                        loc.Location);
                    continue;
                }

                // Extension methods called in instance style (receiver.Foo(args)) exclude
                // the receiver from the arg list, so the positional index must be adjusted.
                var callSiteParamIndex = methodSymbol.IsExtensionMethod
                                         && argList.Arguments.Count < methodSymbol.Parameters.Length
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

    private TextChange[] BuildParameterRemovalChanges(
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

    private ArgumentListSyntax? FindArgumentList(SyntaxNode node)
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

    private TextChange[] BuildArgumentRemovalChanges(
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
            // Remove the named argument
            return BuildArgRemovalChanges(args, args.IndexOf(namedArg), sourceText);

        // Positional: only remove if we have enough positional args
        if (paramIndex >= args.Count)
            return []; // arg was already using default — nothing to remove

        // Ensure the argument at this index is positional (no name colon)
        var positionalArg = args[paramIndex];
        if (positionalArg.NameColon is not null)
            // It's named but not for our param — skip
            return [];

        return BuildArgRemovalChanges(args, paramIndex, sourceText);
    }

    private TextChange[] BuildArgRemovalChanges(
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
