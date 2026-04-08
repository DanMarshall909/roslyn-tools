using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Models;

namespace RoslynTools.Core;

internal static class SymbolLocator
{
    private static readonly SymbolDisplayFormat TypeFqnFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    /// <summary>
    /// Finds a symbol by simple name across all projects in the solution.
    /// Prompts the user to choose if multiple matches exist.
    /// </summary>
    internal static async Task<ISymbol> FindSymbolAsync(
        Solution solution,
        string symbolName,
        string? kind)
    {
        var filter = KindToFilter(kind);

        // FindDeclarationsAsync operates per-project; aggregate across all projects.
        var allDeclarations = new List<ISymbol>();
        foreach (var project in solution.Projects)
        {
            var found = await SymbolFinder.FindDeclarationsAsync(
                project, symbolName, ignoreCase: false, filter);
            allDeclarations.AddRange(found);
        }

        // Further narrow by kind when filter covers multiple symbol types
        var matches = NarrowByKind(allDeclarations, kind)
            .Select(s => new SymbolMatch
            {
                Symbol = s,
                DisplayName = s.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                ProjectName = s.ContainingAssembly?.Name ?? "?"
            })
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"No symbol named '{symbolName}' found in solution" +
                (kind != null ? $" (kind: {kind})" : "") + ".");
        }

        if (matches.Count == 1)
            return matches[0].Symbol;

        return Disambiguate(matches, symbolName);
    }

    internal static async Task<ISymbol> FindSymbolByFqnAsync(
        Solution solution,
        string fullyQualifiedName,
        string? kind)
    {
        var input = NormalizeFqn(fullyQualifiedName);
        var simpleName = GetSimpleName(input);
        var filter = KindToFilter(kind);

        var allDeclarations = new List<ISymbol>();
        foreach (var project in solution.Projects)
        {
            var found = await SymbolFinder.FindDeclarationsAsync(
                project,
                simpleName,
                ignoreCase: false,
                filter);
            allDeclarations.AddRange(found);
        }

        var matches = NarrowByKind(allDeclarations, kind)
            .Where(s => MatchesFqn(s, input))
            .Select(s => new SymbolMatch
            {
                Symbol = s,
                DisplayName = s.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                ProjectName = s.ContainingAssembly?.Name ?? "?"
            })
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException($"No symbol with FQN '{fullyQualifiedName}' found in solution.");

        if (matches.Count == 1)
            return matches[0].Symbol;

        return Disambiguate(matches, fullyQualifiedName);
    }

    /// <summary>
    /// Finds a symbol by fully-qualified type name (e.g. "ProjectX.Core.Services.BookingService").
    /// </summary>
    internal static async Task<ISymbol> FindTypeByFqnAsync(Solution solution, string fqn)
    {
        var lastDot = fqn.LastIndexOf('.');
        var simpleName = lastDot >= 0 ? fqn[(lastDot + 1)..] : fqn;
        var expectedNamespace = lastDot >= 0 ? fqn[..lastDot] : string.Empty;

        var allDeclarations = new List<ISymbol>();
        foreach (var project in solution.Projects)
        {
            var found = await SymbolFinder.FindDeclarationsAsync(
                project, simpleName, ignoreCase: false, SymbolFilter.Type);
            allDeclarations.AddRange(found);
        }

        var matches = allDeclarations
            .Where(s => s.ContainingNamespace?.ToDisplayString() == expectedNamespace)
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException($"No type with FQN '{fqn}' found in solution.");

        if (matches.Count == 1)
            return matches[0];

        // Multiple assemblies may have the same FQN — let user pick
        var symbolMatches = matches.Select(s => new SymbolMatch
        {
            Symbol = s,
            DisplayName = s.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            ProjectName = s.ContainingAssembly?.Name ?? "?"
        }).ToList();

        return Disambiguate(symbolMatches, fqn);
    }

    private static ISymbol Disambiguate(List<SymbolMatch> matches, string name)
    {
        Console.WriteLine($"\nMultiple symbols match '{name}':");
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            Console.WriteLine($"  [{i}]  {m.Symbol.Kind,-12} {m.DisplayName,-60} ({m.ProjectName})");
        }

        while (true)
        {
            Console.Write($"Enter number [0-{matches.Count - 1}]: ");
            var input = Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int choice) && choice >= 0 && choice < matches.Count)
                return matches[choice].Symbol;
            Console.WriteLine("  Invalid selection, try again.");
        }
    }

    private static SymbolFilter KindToFilter(string? kind) => kind switch
    {
        "namespace" => SymbolFilter.Namespace,
        "type"      => SymbolFilter.Type,
        "method"    => SymbolFilter.Member,
        "property"  => SymbolFilter.Member,
        "field"     => SymbolFilter.Member,
        null        => SymbolFilter.All,
        _           => SymbolFilter.All
    };

    private static IEnumerable<ISymbol> NarrowByKind(IEnumerable<ISymbol> symbols, string? kind) => kind switch
    {
        "method"   => symbols.OfType<IMethodSymbol>(),
        "property" => symbols.OfType<IPropertySymbol>(),
        "field"    => symbols.OfType<IFieldSymbol>(),
        _          => symbols
    };

    private static bool MatchesFqn(ISymbol symbol, string input)
    {
        var exact = NormalizeFqn(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        if (exact == input)
            return true;

        var noSignature = NormalizeFqn(GetNameWithoutSignature(symbol));
        return noSignature == input;
    }

    private static string GetNameWithoutSignature(ISymbol symbol)
    {
        if (symbol is INamespaceSymbol ns)
            return ns.ToDisplayString();

        if (symbol is INamedTypeSymbol type)
            return type.ToDisplayString(TypeFqnFormat);

        var left = symbol.ContainingType != null
            ? symbol.ContainingType.ToDisplayString(TypeFqnFormat)
            : symbol.ContainingNamespace?.ToDisplayString();

        return string.IsNullOrEmpty(left)
            ? symbol.Name
            : left + "." + symbol.Name;
    }

    private static string NormalizeFqn(string value)
    {
        const string globalPrefix = "global::";
        var trimmed = value.Trim();
        return trimmed.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? trimmed[globalPrefix.Length..]
            : trimmed;
    }

    private static string GetSimpleName(string fqn)
    {
        var noParams = fqn.Split('(')[0];
        var lastDot = noParams.LastIndexOf('.');
        return lastDot >= 0 ? noParams[(lastDot + 1)..] : noParams;
    }
}
