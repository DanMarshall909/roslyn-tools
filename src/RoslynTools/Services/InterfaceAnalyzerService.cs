using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Analyzes interfaces for code quality and style
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
/// <summary>
/// Pure analysis functions for interface members: determines which members of an
/// interface a given consumer type actually uses, and generates new interface text
/// from a selected member subset.
/// </summary>
internal class InterfaceAnalyzerService : IRtService
{
    /// <summary>
    ///     Returns the set of member names from <paramref name="interfaceSymbol" /> that are
    ///     actually referenced within the body of <paramref name="consumerSymbol" />.
    /// </summary>
    internal async Task<HashSet<string>> GetUsedMembersAsync(
        Solution solution,
        INamedTypeSymbol consumerSymbol,
        INamedTypeSymbol interfaceSymbol)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        var interfaceFqn = interfaceSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        // Each Locations entry covers one partial declaration -- scan all of them
        foreach (var location in consumerSymbol.Locations.Where(l => l.IsInSource))
        {
            var sourceTree = location.SourceTree;
            if (sourceTree is null)
                continue;

            // Find the document in the solution that owns this syntax tree
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath is not null
                                     && string.Equals(d.FilePath, sourceTree.FilePath,
                                         StringComparison.OrdinalIgnoreCase));

            if (document is null)
                continue;

            var root = await document.GetSyntaxRootAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (root is null || semanticModel is null)
                continue;

            // Find the type declaration node(s) in this tree for this consumer
            var typeDecls = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(t =>
                {
                    var sym = semanticModel.GetDeclaredSymbol(t);
                    return sym is INamedTypeSymbol namedType
                           && namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                           == consumerSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                })
                .ToList();

            foreach (var typeDecl in typeDecls)
                foreach (var simpleName in typeDecl.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(simpleName);
                    var sym = symbolInfo.Symbol
                              ?? (symbolInfo.CandidateSymbols.Length == 1 ? symbolInfo.CandidateSymbols[0] : null);

                    if (sym is null)
                        continue;

                    // Normalize accessors back to their parent member
                    sym = sym switch
                    {
                        IMethodSymbol
                        {
                            MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet
                            or MethodKind.EventAdd or MethodKind.EventRemove,
                            AssociatedSymbol: { } assoc
                        } => assoc,
                        _ => sym
                    };

                    // Match by containing-type FQN to avoid cross-compilation reference issues
                    var containingFqn = sym.ContainingType?
                        .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

                    if (containingFqn != interfaceFqn)
                        continue;

                    used.Add(sym.Name);
                }
        }

        return used;
    }
}
