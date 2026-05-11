using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
/// <summary>
/// Rewriting logic for narrowing consumers: finds all types that depend on a wide
/// interface and rewrites those that only use a selected member subset to use the
/// new narrower interface instead.
/// </summary>
internal class ConsumerNarrowerService : IRtService
{
    private readonly InterfaceAnalyzerService _interfaceAnalyzer;

    public ConsumerNarrowerService()
        : this(new InterfaceAnalyzerService())
    {
    }

    public ConsumerNarrowerService(InterfaceAnalyzerService interfaceAnalyzer)
    {
        _interfaceAnalyzer = interfaceAnalyzer;
    }

    /// <summary>
    ///     Finds all types in the solution that hold a field or constructor parameter
    ///     typed as <paramref name="interfaceSymbol" />.
    /// </summary>
    internal async Task<List<(INamedTypeSymbol Symbol, Document Document)>> FindConsumersAsync(
        Solution solution,
        INamedTypeSymbol interfaceSymbol)
    {
        var result = new List<(INamedTypeSymbol, Document)>();

        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                var semanticModel = await document.GetSemanticModelAsync();
                if (root is null || semanticModel is null)
                    continue;

                // Find fields typed as the interface
                foreach (var field in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                    if (semanticModel.GetDeclaredSymbol(field) is IFieldSymbol fieldSym
                        && SymbolEqualityComparer.Default.Equals(fieldSym.Type, interfaceSymbol))
                    {
                        var containingType = fieldSym.ContainingType;
                        if (containingType is not null) result.Add((containingType, document));
                    }

                // Find ctor parameters typed as the interface
                foreach (var param in root.DescendantNodes().OfType<ParameterSyntax>())
                    if (semanticModel.GetDeclaredSymbol(param) is IParameterSymbol paramSym
                        && SymbolEqualityComparer.Default.Equals(paramSym.Type, interfaceSymbol)
                        && paramSym.ContainingSymbol is IMethodSymbol
                        {
                            MethodKind: MethodKind.Constructor
                        } ctorSym)
                    {
                        var containingType = ctorSym.ContainingType;
                        if (containingType is not null) result.Add((containingType, document));
                    }
            }

        // Deduplicate by type display name
        return result
            .GroupBy(r => r.Item1.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    ///     For each consumer that exclusively uses the selected member subset, rewrites
    ///     its type annotations from the old interface name to the new one and returns
    ///     the updated solution together with lists of narrowed and skipped consumers.
    /// </summary>
    internal async Task<NarrowResult> NarrowConsumersAsync(
        Solution originalSolution,
        INamedTypeSymbol interfaceSymbol,
        List<ISymbol> selectedMembers,
        string newInterfaceName,
        Solution workingSolution)
    {
        var selectedMemberNames = selectedMembers
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        var narrowed = new List<string>();
        var skipped = new List<(string Name, string Reason)>();

        // Find all classes/structs that have a field or ctor param of the interface type
        var consumers = await FindConsumersAsync(originalSolution, interfaceSymbol);

        foreach (var (consumerSymbol, consumerDoc) in consumers)
        {
            // Determine which interface members this consumer actually uses
            var usedMembers = await _interfaceAnalyzer.GetUsedMembersAsync(
                originalSolution, consumerSymbol, interfaceSymbol);

            // Check if all used members are within the selected subset
            var usedOutside = usedMembers
                .Where(m => !selectedMemberNames.Contains(m))
                .ToList();

            if (usedOutside.Count > 0)
            {
                skipped.Add((
                    consumerSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    $"uses members outside the selected subset: {string.Join(", ", usedOutside)}"));
                continue;
            }

            // Narrow the consumer document
            var updatedDoc = await NarrowConsumerDocumentAsync(
                workingSolution,
                consumerDoc,
                consumerSymbol,
                interfaceSymbol.Name,
                newInterfaceName);

            if (updatedDoc is not null)
            {
                workingSolution = updatedDoc;
                narrowed.Add(consumerSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            }
        }

        return new NarrowResult(workingSolution, narrowed, skipped);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<Solution?> NarrowConsumerDocumentAsync(
        Solution solution,
        Document consumerDocument,
        INamedTypeSymbol consumerSymbol,
        string oldInterfaceName,
        string newInterfaceName)
    {
        var root = await consumerDocument.GetSyntaxRootAsync();
        var semanticModel = await consumerDocument.GetSemanticModelAsync();
        if (root is null || semanticModel is null)
            return null;

        var rewriter = new NarrowTypeRewriter(semanticModel, oldInterfaceName, newInterfaceName);
        var newRoot = rewriter.Visit(root);

        if (newRoot is null || newRoot.IsEquivalentTo(root))
            return null;

        return solution.WithDocumentSyntaxRoot(consumerDocument.Id, newRoot);
    }

    internal sealed record NarrowResult(
        Solution Solution,
        List<string> NarrowedConsumers,
        List<(string Name, string Reason)> SkippedConsumers);

    // -------------------------------------------------------------------------
    // Syntax rewriter -- replaces type annotations of the old interface name
    // -------------------------------------------------------------------------

    private sealed class NarrowTypeRewriter : CSharpSyntaxRewriter
    {
        private readonly string _newName;
        private readonly string _oldName;
        private readonly SemanticModel _semanticModel;

        internal NarrowTypeRewriter(SemanticModel semanticModel, string oldName, string newName)
        {
            _semanticModel = semanticModel;
            _oldName = oldName;
            _newName = newName;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Identifier.Text != _oldName)
                return base.VisitIdentifierName(node);

            // Only rewrite if the semantic model confirms this identifier refers to the interface type
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            var sym = symbolInfo.Symbol ??
                      (symbolInfo.CandidateSymbols.Length == 1 ? symbolInfo.CandidateSymbols[0] : null);

            if (sym is INamedTypeSymbol namedType
                && namedType.TypeKind == TypeKind.Interface
                && namedType.Name == _oldName)
                return node.WithIdentifier(
                    SyntaxFactory.Identifier(node.Identifier.LeadingTrivia, _newName, node.Identifier.TrailingTrivia));

            return base.VisitIdentifierName(node);
        }
    }
}
