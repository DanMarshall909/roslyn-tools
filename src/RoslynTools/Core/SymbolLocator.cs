using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core.Decisions;
using RoslynTools.Models;

namespace RoslynTools.Core;

/// <summary>
/// Locates symbols in a solution.
/// </summary>
internal static class SymbolLocator
{
    private const int MaximumAmbiguityCandidates = 12;
    private const int MaximumAmbiguityContextBytes = 4_096;
    private const int MaximumAmbiguityDisplayBytes = 1_024;
    private const int MaximumAmbiguityQueryBytes = 256;

    private static readonly SymbolDisplayFormat TypeFqnFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    /// <summary>
    ///     Finds a symbol by simple name across all projects in the solution, returning a typed
    ///     <see cref="Result{T,TError}" /> for the routine not-found / ambiguous-match flows instead
    ///     of throwing. A genuinely interactive disambiguation (a decisions store, or a TTY that
    ///     supplies a choice) is honoured as before — only the store path can still throw
    ///     <see cref="DecisionsRequiredException" /> (the exit-2 mechanism), which is not an
    ///     <see cref="RtError" />.
    /// </summary>
    internal static async Task<Result<ISymbol, RtError>> TryFindSymbolAsync(
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
            allDeclarations.AddRange(await FindCompilationSymbolsByNameAsync(project, symbolName, filter));
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
            return Result<ISymbol, RtError>.Err(RtError.NotFound(
                $"No symbol named '{symbolName}' found in solution" +
                (kind != null ? $" (kind: {kind})" : "") + "."));

        if (matches.Count == 1)
            return Result<ISymbol, RtError>.Ok(matches[0].Symbol);

        return TryDisambiguate(matches, symbolName);
    }

    /// <summary>
    ///     Finds a symbol by simple name. Throwing bridge over <see cref="TryFindSymbolAsync" />
    ///     for callers not yet migrated to consume <see cref="Result{T,TError}" />; preserves the
    ///     historical exception message.
    /// </summary>
    internal static async Task<ISymbol> FindSymbolAsync(
        Solution solution,
        string symbolName,
        string? kind)
    {
        return (await TryFindSymbolAsync(solution, symbolName, kind)).Unwrap();
    }

    /// <summary>
    ///     Finds a symbol by its fully-qualified name, returning a typed <see cref="Result{T,TError}" />
    ///     for the routine not-found / ambiguous-match flows instead of throwing. A genuinely
    ///     interactive disambiguation (a decisions store, or a TTY that supplies a choice) is honoured
    ///     as before — only the store path can still throw <see cref="DecisionsRequiredException" />
    ///     (the exit-2 mechanism), which is not an <see cref="RtError" />.
    /// </summary>
    internal static async Task<Result<ISymbol, RtError>> TryFindSymbolByFqnAsync(
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
            return Result<ISymbol, RtError>.Err(
                RtError.NotFound($"No symbol with FQN '{fullyQualifiedName}' found in solution."));

        if (matches.Count == 1)
            return Result<ISymbol, RtError>.Ok(matches[0].Symbol);

        return TryDisambiguate(matches, fullyQualifiedName);
    }

    /// <summary>
    ///     Finds a symbol by its fully-qualified name. Throwing bridge over
    ///     <see cref="TryFindSymbolByFqnAsync" /> for callers not yet migrated to consume
    ///     <see cref="Result{T,TError}" />; preserves the historical exception message.
    /// </summary>
    internal static async Task<ISymbol> FindSymbolByFqnAsync(
        Solution solution,
        string fullyQualifiedName,
        string? kind)
    {
        return (await TryFindSymbolByFqnAsync(solution, fullyQualifiedName, kind)).Unwrap();
    }

    /// <summary>
    ///     Finds a symbol by fully-qualified type name (e.g. "ProjectX.Core.Services.BookingService"),
    ///     returning a typed <see cref="Result{T,TError}" /> for the routine not-found / ambiguous-match
    ///     flows instead of throwing. A genuinely interactive disambiguation (a decisions store, or a
    ///     TTY that supplies a choice) is honoured as before — only the store path can still throw
    ///     <see cref="DecisionsRequiredException" /> (the exit-2 mechanism), which is not an
    ///     <see cref="RtError" />.
    /// </summary>
    internal static async Task<Result<ISymbol, RtError>> TryFindTypeByFqnAsync(Solution solution, string fqn)
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

        if (symbolMatches.Count == 0)
            return Result<ISymbol, RtError>.Err(
                RtError.NotFound($"No type with FQN '{fqn}' found in solution."));

        if (symbolMatches.Count == 1)
            return Result<ISymbol, RtError>.Ok(symbolMatches[0].Symbol);

        return TryDisambiguate(symbolMatches, fqn);
    }

    /// <summary>
    ///     Finds a symbol by fully-qualified type name. Throwing bridge over
    ///     <see cref="TryFindTypeByFqnAsync" /> for callers not yet migrated to consume
    ///     <see cref="Result{T,TError}" />; preserves the historical exception message.
    /// </summary>
    internal static async Task<ISymbol> FindTypeByFqnAsync(Solution solution, string fqn)
    {
        return (await TryFindTypeByFqnAsync(solution, fqn)).Unwrap();
    }

    /// <summary>
    ///     Finds a method by its specification, returning a typed <see cref="Result{T,TError}" />
    ///     for the routine not-found / ambiguous-match flows instead of throwing. A genuinely
    ///     interactive disambiguation (a decisions store, or a TTY that supplies a choice) is honoured
    ///     as before — only the store path can still throw <see cref="DecisionsRequiredException" />
    ///     (the exit-2 mechanism), which is not an <see cref="RtError" />.
    /// </summary>
    internal static async Task<Result<IMethodSymbol, RtError>> TryFindMethodBySpecAsync(
        Solution solution,
        string methodSpec)
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
            return Result<IMethodSymbol, RtError>.Err(
                RtError.NotFound($"Method '{methodSpec}' not found in solution."));

        if (matches.Count == 1)
            return Result<IMethodSymbol, RtError>.Ok((IMethodSymbol)matches[0].Symbol);

        return TryDisambiguate(matches, methodSpec).Map(s => (IMethodSymbol)s);
    }

    /// <summary>
    ///     Finds a method by its specification. Throwing bridge over
    ///     <see cref="TryFindMethodBySpecAsync" /> for callers not yet migrated to consume
    ///     <see cref="Result{T,TError}" />; preserves the historical exception message.
    /// </summary>
    internal static async Task<IMethodSymbol> FindMethodBySpecAsync(Solution solution, string methodSpec)
    {
        return (await TryFindMethodBySpecAsync(solution, methodSpec)).Unwrap();
    }

    /// <summary>
    ///     Resolves a single member of a type from a signature drill-in input of the form
    ///     <c>&lt;TypeFqn&gt;.&lt;Name&gt;(&lt;paramTypes&gt;)</c> (US-135). The containing type is
    ///     resolved via <see cref="TryFindTypeByFqnAsync" />; the member is matched within it by
    ///     comparing <see cref="SignatureFormatter.MatchSignature" /> against the supplied param list,
    ///     so a signature emitted in the default describe/outline output round-trips back to its member.
    ///     A name with no parenthesised param list matches by name alone: unique resolves, overloads
    ///     return <see cref="RtErrorKind.AmbiguousMatch" /> with the candidate signatures listed.
    /// </summary>
    internal static async Task<Result<ISymbol, RtError>> TryFindMemberBySignatureAsync(
        Solution solution,
        string input,
        bool includeSuggestions = true)
    {
        var (typeFqn, memberName, paramSpec) = ParseMemberSignatureInput(input);
        if (string.IsNullOrEmpty(typeFqn) || string.IsNullOrEmpty(memberName))
            return Result<ISymbol, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput,
                    $"Could not parse member signature '{input}'. "
                    + "Expected '<TypeFqn>.<Name>(<paramTypes>)'."));

        var typeResult = await TryFindTypeByFqnAsync(solution, typeFqn);
        if (typeResult.IsErr)
            return typeResult;

        var type = (INamedTypeSymbol)typeResult.Match(s => s, _ => null!);

        var byName = type.GetMembers(memberName)
            .Where(m => !m.IsImplicitlyDeclared)
            .ToList();

        if (byName.Count == 0)
            return Result<ISymbol, RtError>.Err(
                RtError.NotFound(BuildMemberNotFoundMessage(type, memberName, typeFqn, includeSuggestions)));

        List<ISymbol> matches;
        if (paramSpec is null)
        {
            matches = byName;
        }
        else
        {
            var normalizedSpec = NormalizeSignature(memberName + paramSpec);
            matches = byName
                .Where(m => NormalizeSignature(SignatureFormatter.MatchSignature(m)) == normalizedSpec)
                .ToList();
        }

        if (matches.Count == 0)
            return Result<ISymbol, RtError>.Err(
                RtError.NotFound($"No member matching '{input}' found on type '{typeFqn}'."));

        if (matches.Count == 1)
            return Result<ISymbol, RtError>.Ok(matches[0]);

        return Result<ISymbol, RtError>.Err(BoundedAmbiguousMatch(
            input,
            matches.Select(SignatureFormatter.Signature),
            typeFqn));
    }

    /// <summary>
    ///     Splits a drill-in input into its type FQN, member name, and parenthesised param spec
    ///     ("(int, int)"); the param spec is null when the input carries no parentheses (name-only).
    ///     Tolerates a stray return-type prefix by keeping only the token after the last whitespace
    ///     before the member name.
    /// </summary>
    private static (string TypeFqn, string Name, string? ParamSpec) ParseMemberSignatureInput(string input)
    {
        var trimmed = NormalizeFqn(input);
        var parenIndex = trimmed.IndexOf('(');

        string head;
        string? paramSpec;
        if (parenIndex >= 0)
        {
            head = trimmed[..parenIndex].TrimEnd();
            paramSpec = trimmed[parenIndex..];
        }
        else
        {
            head = trimmed;
            paramSpec = null;
        }

        var lastSpace = head.LastIndexOf(' ');
        if (lastSpace >= 0)
            head = head[(lastSpace + 1)..];

        var lastDot = head.LastIndexOf('.');
        if (lastDot < 0)
            return (string.Empty, string.Empty, paramSpec);

        return (head[..lastDot], head[(lastDot + 1)..], paramSpec);
    }

    private static string NormalizeSignature(string signature)
    {
        return new string(signature.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    /// <summary>
    ///     The decisions-store path is unchanged (it may throw <see cref="DecisionsRequiredException" />
    ///     for the exit-2 flow); without a decisions store this returns a typed
    ///     <see cref="RtErrorKind.AmbiguousMatch" /> error that lists the candidate FQNs.
    /// </summary>
    private static Result<ISymbol, RtError> TryDisambiguate(List<SymbolMatch> matches, string name)
    {
        var store = DecisionContext.Store;
        if (store is not null)
            return Result<ISymbol, RtError>.Ok(DisambiguateViaStore(matches, name, store));

        return AmbiguousMatch(matches, name);
    }

    private static Result<ISymbol, RtError> AmbiguousMatch(List<SymbolMatch> matches, string name)
    {
        return Result<ISymbol, RtError>.Err(BoundedAmbiguousMatch(
            name,
            matches.Select(match => match.DisplayName)));
    }

    private static RtError BoundedAmbiguousMatch(
        string name,
        IEnumerable<string> candidateNames,
        string? containingType = null)
    {
        var candidates = candidateNames.ToArray();
        var listed = new List<string>();
        var contextBytes = 0;
        var displayBytes = 0;
        foreach (var candidate in candidates)
        {
            if (listed.Count == MaximumAmbiguityCandidates)
                break;

            var nextContextBytes = contextBytes + Encoding.UTF8.GetByteCount(candidate)
                                   + (listed.Count == 0 ? 0 : 1);
            var nextDisplayBytes = displayBytes + Encoding.UTF8.GetByteCount(candidate)
                                   + (listed.Count == 0 ? 0 : 2);
            if (nextContextBytes > MaximumAmbiguityContextBytes
                || nextDisplayBytes > MaximumAmbiguityDisplayBytes)
                break;

            listed.Add(candidate);
            contextBytes = nextContextBytes;
            displayBytes = nextDisplayBytes;
        }

        var query = LimitUtf8(name, MaximumAmbiguityQueryBytes);
        var memberAmbiguity = containingType is not null;
        var guidance = memberAmbiguity
            ? "Pass an explicit parameter signature."
            : "Pass a more specific fully-qualified name.";
        var candidateCount = candidates.Length.ToString(CultureInfo.InvariantCulture);
        var displayedCount = listed.Count.ToString(CultureInfo.InvariantCulture);
        var context = new Dictionary<string, string>
        {
            ["symbol"] = query,
            ["symbolTruncated"] = query == name ? "false" : "true",
            ["candidateCount"] = candidateCount,
            ["displayedCandidateCount"] = displayedCount,
            ["candidatesTruncated"] = listed.Count < candidates.Length ? "true" : "false",
            ["candidates"] = string.Join(';', listed),
            ["disambiguateWith"] = guidance
        };
        if (containingType is not null)
            context["type"] = LimitUtf8(containingType, MaximumAmbiguityQueryBytes);

        var summary = listed.Count == 0
            ? "No candidate fits within the response budget"
            : string.Join(memberAmbiguity ? "; " : ", ", listed);
        var prefix = containingType is null
            ? $"Multiple symbols match '{query}': "
            : $"'{query}' is ambiguous on type '{context["type"]}'. Candidates: ";
        return RtError.AmbiguousMatch(
            $"{prefix}{summary}. "
            + $"{candidateCount} candidates; showing {displayedCount}. "
            + guidance,
            context);
    }

    private static string LimitUtf8(string value, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
            return value;

        const char ellipsis = '…';
        var remaining = maximumBytes - Encoding.UTF8.GetByteCount(ellipsis.ToString());
        var builder = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > remaining)
                break;
            builder.Append(rune.ToString());
            remaining -= rune.Utf8SequenceLength;
        }

        return builder.Append(ellipsis).ToString();
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

    private static async Task<IEnumerable<ISymbol>> FindCompilationSymbolsByNameAsync(
        Project project,
        string symbolName,
        SymbolFilter filter)
    {
        var compilation = await project.GetCompilationAsync();
        if (compilation is null)
            return [];

        return compilation.GetSymbolsWithName(
                name => string.Equals(name, symbolName, StringComparison.Ordinal),
                filter)
            .Where(IsSourceDeclaration);
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
                && candidateSegments.AsSpan(candidateSegments.Length - specSegments.Length).SequenceEqual(specSegments))
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
    ///     US-135 Batch 2: build the not-found message for a drill-in member miss, appending a
    ///     "Did you mean: ..." tail when <see cref="NameSuggest" /> finds one or more candidate
    ///     member names within the length-relative edit-distance threshold. The tail is
    ///     suppressed when the caller opts out of suggestions, so strict-output
    ///     pipelines can pin error text.
    /// </summary>
    private static string BuildMemberNotFoundMessage(
        INamedTypeSymbol type,
        string memberName,
        string typeFqn,
        bool includeSuggestions)
    {
        var baseMessage = $"Member '{memberName}' not found on type '{typeFqn}'.";
        if (!includeSuggestions)
            return baseMessage;

        var candidateNames = type.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared && m.CanBeReferencedByName)
            .Select(m => m.Name);
        var nearest = NameSuggest.NearestNames(memberName, candidateNames, max: 3);
        if (nearest.Count == 0)
            return baseMessage;

        return baseMessage + " Did you mean: " + string.Join(", ", nearest) + "?";
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
