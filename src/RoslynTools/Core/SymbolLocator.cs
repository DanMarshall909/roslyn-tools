using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core.Decisions;
using RoslynTools.Models;

namespace RoslynTools.Core;

internal static class SymbolLocator
{
    private static readonly SymbolDisplayFormat TypeFqnFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    /// <summary>
    ///     Finds a symbol by simple name across all projects in the solution.
    ///     Prompts the user to choose if multiple matches exist.
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
                project, symbolName, false, filter);
            allDeclarations.AddRange(found.Where(IsSourceDeclaration));
        }

        // Further narrow by kind when filter covers multiple symbol types.
        // Deduplicate: same FQN appearing in multiple projects (e.g. main + test) collapses to one.
        var matches = NarrowByKind(allDeclarations, kind)
            .Select(s => new SymbolMatch
            {
                Symbol = s,
                DisplayName = s.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                ProjectName = s.ContainingAssembly?.Name ?? "?"
            })
            .GroupBy(m => m.DisplayName)
            .Select(g => g.First())
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"No symbol named '{symbolName}' found in solution" +
                (kind != null ? $" (kind: {kind})" : "") + ".");

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
                false,
                filter);
            allDeclarations.AddRange(found.Where(IsSourceDeclaration));
        }

        var matches = NarrowByKind(allDeclarations, kind)
            .Where(s => MatchesFqn(s, input))
            .Select(s => new SymbolMatch
            {
                Symbol = s,
                DisplayName = s.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                ProjectName = s.ContainingAssembly?.Name ?? "?"
            })
            // Deduplicate: same FQN appearing in multiple projects (e.g. main + test) collapses to one.
            .GroupBy(m => m.DisplayName)
            .Select(g => g.First())
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException($"No symbol with FQN '{fullyQualifiedName}' found in solution.");

        if (matches.Count == 1)
            return matches[0].Symbol;

        return Disambiguate(matches, fullyQualifiedName);
    }

    /// <summary>
    ///     Finds a symbol by fully-qualified type name (e.g. "ProjectX.Core.Services.BookingService").
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
                project, simpleName, false, SymbolFilter.Type);
            allDeclarations.AddRange(found.Where(IsSourceDeclaration));
        }

        // Deduplicate: same FQN appearing in multiple projects (e.g. main + test) collapses to one.
        var symbolMatches = allDeclarations
            .Where(s => s.ContainingNamespace?.ToDisplayString() == expectedNamespace)
            .Select(s => new SymbolMatch
            {
                Symbol = s,
                DisplayName = s.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                ProjectName = s.ContainingAssembly?.Name ?? "?"
            })
            .GroupBy(m => m.DisplayName)
            .Select(g => g.First())
            .ToList();

        // Re-extract the raw symbols list for the single-match fast path
        var matches = symbolMatches.Select(m => m.Symbol).ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException($"No type with FQN '{fqn}' found in solution.");

        if (matches.Count == 1)
            return matches[0];

        return Disambiguate(symbolMatches, fqn);
    }

    internal static async Task<IMethodSymbol> FindMethodBySpecAsync(Solution solution, string methodSpec)
    {
        var input = NormalizeFqn(methodSpec);
        var inputWithoutSignature = RemoveSignature(input);
        var specSegments = inputWithoutSignature.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<SymbolMatch>();

        foreach (var project in solution.Projects)
        foreach (var document in project.Documents)
        {
            var root = await document.GetSyntaxRootAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (root is null || semanticModel is null)
                continue;

            foreach (var declaration in root.DescendantNodes().Where(IsMethodLikeDeclaration))
            {
                var method = semanticModel.GetDeclaredSymbol(declaration) as IMethodSymbol;
                if (method is null || !IsSupportedMethod(method))
                    continue;

                if (!MatchesMethodSpec(method, input, inputWithoutSignature, specSegments))
                    continue;

                matches.Add(new SymbolMatch
                {
                    Symbol = method,
                    DisplayName = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    ProjectName = method.ContainingAssembly?.Name ?? "?"
                });
            }
        }

        matches = matches
            .GroupBy(match => match.DisplayName, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException($"Method '{methodSpec}' not found in solution.");

        if (matches.Count == 1)
            return (IMethodSymbol)matches[0].Symbol;

        return (IMethodSymbol)Disambiguate(matches, methodSpec);
    }

    private static ISymbol Disambiguate(List<SymbolMatch> matches, string name)
    {
        var store = DecisionContext.Store;
        if (store is not null)
            return DisambiguateViaStore(matches, name, store);

        RtConsole.WriteLine(RtOutputContext.Interactive, $"\nMultiple symbols match '{name}':");
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            RtConsole.WriteLine(RtOutputContext.Interactive,
                $"  [{i}]  {m.Symbol.Kind,-12} {m.DisplayName,-60} ({m.ProjectName})");
        }

        while (true)
        {
            RtConsole.Write(RtOutputContext.Interactive, $"Enter number [0-{matches.Count - 1}]: ");
            var rawInput = RtConsole.ReadLine();
            if (rawInput is null)
                throw new InvalidOperationException(
                    $"Multiple symbols match '{name}', but no selection was provided. "
                    + "Pass a more specific symbol or provide input for the prompt.");

            var input = rawInput.Trim();
            if (int.TryParse(input, out var choice) && choice >= 0 && choice < matches.Count)
                return matches[choice].Symbol;
            RtConsole.WriteLine(RtOutputContext.Interactive, "  Invalid selection, try again.");
        }
    }

    private static ISymbol DisambiguateViaStore(List<SymbolMatch> matches, string name, DecisionStore store)
    {
        var id = $"ambig-{name}-{matches.Count}";

        if (store.TryGetAnswer(id, out var choice))
        {
            if (choice < 0 || choice >= matches.Count)
                throw new InvalidOperationException(
                    $"Invalid choice for {id}: {choice}. Expected 0-{matches.Count - 1}.");
            return matches[choice].Symbol;
        }

        var entry = new DecisionEntry
        {
            Id = id,
            Q = $"{matches.Count} symbols match '{name}'",
            Options = new Dictionary<int, string>(matches.Count)
        };
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            entry.Options[i] = $"{m.Symbol.Kind,-12} {m.DisplayName,-60} ({m.ProjectName})";
        }

        store.Add(entry);

        throw new DecisionsRequiredException(
            $"Multiple symbols match '{name}'; resolve in decisions file and re-run.");
    }

    private static SymbolFilter KindToFilter(string? kind)
    {
        return kind switch
        {
            "namespace" => SymbolFilter.Namespace,
            "type" => SymbolFilter.Type,
            "method" => SymbolFilter.Member,
            "property" => SymbolFilter.Member,
            "field" => SymbolFilter.Member,
            null => SymbolFilter.All,
            _ => SymbolFilter.All
        };
    }

    private static bool IsSourceDeclaration(ISymbol symbol)
    {
        return symbol.DeclaringSyntaxReferences.Length > 0;
    }

    private static IEnumerable<ISymbol> NarrowByKind(IEnumerable<ISymbol> symbols, string? kind)
    {
        return kind switch
        {
            "namespace" => symbols.OfType<INamespaceSymbol>(),
            "type" => symbols.OfType<INamedTypeSymbol>(),
            "method" => symbols.OfType<IMethodSymbol>(),
            "property" => symbols.OfType<IPropertySymbol>(),
            "field" => symbols.OfType<IFieldSymbol>(),
            _ => symbols
        };
    }

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

    private static bool IsMethodLikeDeclaration(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax or ConstructorDeclarationSyntax or LocalFunctionStatementSyntax;
    }

    private static bool IsSupportedMethod(IMethodSymbol method)
    {
        return method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.LocalFunction;
    }

    private static bool MatchesMethodSpec(
        IMethodSymbol method,
        string input,
        string inputWithoutSignature,
        string[] specSegments)
    {
        var displayName = NormalizeFqn(method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        if (displayName == input)
            return true;

        foreach (var candidate in GetMethodSpecCandidates(method))
        {
            if (candidate == inputWithoutSignature)
                return true;

            var candidateSegments = candidate.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (candidateSegments.Length >= specSegments.Length
                && candidateSegments[^specSegments.Length..].SequenceEqual(specSegments, StringComparer.Ordinal))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> GetMethodSpecCandidates(IMethodSymbol method)
    {
        yield return string.Join(".", BuildMethodPathSegments(method, false));

        if (method.MethodKind == MethodKind.Constructor)
            yield return string.Join(".", BuildMethodPathSegments(method, true));
    }

    private static string[] BuildMethodPathSegments(IMethodSymbol method, bool constructorUsesTypeName)
    {
        var segments = new List<string>();

        if (!method.ContainingNamespace.IsGlobalNamespace)
            segments.AddRange(method.ContainingNamespace.ToDisplayString()
                .Split('.', StringSplitOptions.RemoveEmptyEntries));

        segments.AddRange(GetContainingTypeNames(method.ContainingType));
        segments.AddRange(GetContainingCallableNames(method.ContainingSymbol));
        segments.Add(GetMethodSegment(method, constructorUsesTypeName));

        return [.. segments];
    }

    private static string[] GetContainingTypeNames(INamedTypeSymbol? type)
    {
        if (type is null)
            return [];

        var stack = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
            stack.Push(current.Name);

        return [.. stack];
    }

    private static string[] GetContainingCallableNames(ISymbol? symbol)
    {
        var stack = new Stack<string>();

        for (var current = symbol; current is IMethodSymbol method; current = current.ContainingSymbol)
        {
            if (method.MethodKind == MethodKind.LocalFunction)
            {
                stack.Push(method.Name);
                continue;
            }

            if (method.MethodKind == MethodKind.Ordinary) stack.Push(method.Name);

            break;
        }

        return [.. stack];
    }

    private static string GetMethodSegment(IMethodSymbol method, bool constructorUsesTypeName)
    {
        return method.MethodKind == MethodKind.Constructor && constructorUsesTypeName
            ? method.ContainingType.Name
            : method.Name;
    }

    private static string RemoveSignature(string value)
    {
        var parenIndex = value.IndexOf('(');
        return parenIndex >= 0 ? value[..parenIndex] : value;
    }

    /// <summary>
    ///     Finds the first method with the given name in the named type, searching across all projects.
    ///     Returns null when not found.
    /// </summary>
    internal static async Task<IMethodSymbol?> FindMethodInTypeAsync(
        Solution sln, string typeName, string methodName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var symbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
                if (symbol is INamedTypeSymbol typeSymbol)
                {
                    var method = typeSymbol.GetMembers(methodName)
                        .OfType<IMethodSymbol>()
                        .FirstOrDefault();
                    if (method != null) return method;
                }
        }

        return null;
    }
}
