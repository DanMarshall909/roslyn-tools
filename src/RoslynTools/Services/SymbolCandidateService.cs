using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record SymbolCandidateQuery(
    string Query,
    string? Kind = null,
    string? Project = null,
    string? Namespace = null,
    string? Accessibility = null,
    int? GenericArity = null,
    string? Signature = null,
    bool Fuzzy = false,
    string? SourceRevision = null);

internal sealed record ResolvedSymbolCandidate(ISymbol Symbol, SymbolCandidate Candidate);

internal interface ISymbolCandidateService
{
    Task<IReadOnlyList<ResolvedSymbolCandidate>> SearchAsync(
        Solution solution,
        SymbolCandidateQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ISymbol, RtError>> ResolveAsync(
        Solution solution,
        string identityOrName,
        string? kind,
        CancellationToken cancellationToken = default);

    Task<Result<ISymbol, RtError>> ResolveAsync(
        Solution solution,
        string identityOrName,
        string? kind,
        string sourceRevision,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     One deterministic symbol-candidate implementation shared by public search and
///     focused consumers. It preserves same-FQN declarations from different projects
///     instead of silently collapsing them.
/// </summary>
internal sealed class SymbolCandidateService : ISymbolCandidateService, IRtService
{
    public async Task<IReadOnlyList<ResolvedSymbolCandidate>> SearchAsync(
        Solution solution,
        SymbolCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<ResolvedSymbolCandidate>();
        var sourceRevision = query.SourceRevision
                             ?? await SolutionRevision.ComputeAsync(solution, cancellationToken);
        foreach (var project in solution.Projects.OrderBy(project => project.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(query.Project, project.Name))
                continue;
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
                continue;

            foreach (var symbol in EnumerateSourceSymbols(
                         compilation.Assembly.GlobalNamespace,
                         cancellationToken))
            {
                if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly))
                    continue;
                var candidate = CreateCandidate(
                    project,
                    symbol,
                    query.Query,
                    query.Fuzzy,
                    sourceRevision);
                if (candidate is null
                    || !Matches(query.Kind, candidate.Kind)
                    || !Matches(query.Namespace, candidate.Namespace)
                    || !Matches(query.Accessibility, candidate.Accessibility)
                    || query.GenericArity is { } arity && candidate.GenericArity != arity
                    || query.Signature is { Length: > 0 } signature
                    && !candidate.Signature.Contains(signature, StringComparison.OrdinalIgnoreCase))
                    continue;
                candidates.Add(new ResolvedSymbolCandidate(symbol, candidate));
            }
        }

        var bestRank = candidates.Count == 0
            ? -1
            : candidates.Min(candidate => candidate.Candidate.Rank);
        var rankedCandidates = bestRank is >= 0 and <= 2
            ? candidates.Where(candidate => candidate.Candidate.Rank == bestRank)
            : candidates;
        return rankedCandidates
            .GroupBy(candidate => candidate.Candidate.SymbolId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.Candidate.Rank)
            .ThenBy(candidate => candidate.Candidate.Fqn, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Candidate.Project, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<Result<ISymbol, RtError>> ResolveAsync(
        Solution solution,
        string identityOrName,
        string? kind,
        CancellationToken cancellationToken = default)
    {
        var sourceRevision = await SolutionRevision.ComputeAsync(solution, cancellationToken);
        return await ResolveAsync(
            solution,
            identityOrName,
            kind,
            sourceRevision,
            cancellationToken);
    }

    public async Task<Result<ISymbol, RtError>> ResolveAsync(
        Solution solution,
        string identityOrName,
        string? kind,
        string sourceRevision,
        CancellationToken cancellationToken = default)
    {
        if (TryGetIdentityRevision(identityOrName, out var identityRevision)
            && !string.Equals(identityRevision, sourceRevision, StringComparison.Ordinal))
        {
            return Result<ISymbol, RtError>.Err(new RtError(
                RtErrorKind.Stale,
                "The symbol identity belongs to an older or different source revision. Search again before continuing.",
                new Dictionary<string, string>
                {
                    ["identityRevision"] = identityRevision,
                    ["currentRevision"] = sourceRevision
                }));
        }

        var candidates = await SearchAsync(
            solution,
            new SymbolCandidateQuery(
                identityOrName,
                Kind: NormalizeKind(kind),
                SourceRevision: sourceRevision),
            cancellationToken);
        if (candidates.Count == 0)
        {
            return Result<ISymbol, RtError>.Err(RtError.NotFound(
                $"No symbol matching '{identityOrName}' was found in the loaded solution.",
                new Dictionary<string, string> { ["symbol"] = identityOrName }));
        }

        if (candidates.Count == 1)
            return Result<ISymbol, RtError>.Ok(candidates[0].Symbol);

        var context = new Dictionary<string, string>
        {
            ["symbol"] = identityOrName,
            ["candidates"] = string.Join(
                Environment.NewLine,
                candidates.Select(candidate =>
                    $"{candidate.Candidate.SymbolId} | {candidate.Candidate.Fqn} | {candidate.Candidate.Project}")),
            ["disambiguateWith"] = "stable symbol identity, project, namespace, kind, generic arity, or signature"
        };
        var displayCandidates = string.Join(
            ", ",
            candidates.Select(candidate =>
                $"{candidate.Candidate.Fqn} [{candidate.Candidate.Project}]"));
        return Result<ISymbol, RtError>.Err(RtError.AmbiguousMatch(
            $"Multiple symbols match '{identityOrName}': {displayCandidates}.",
            context));
    }

    private static IEnumerable<ISymbol> EnumerateSourceSymbols(
        INamespaceSymbol root,
        CancellationToken cancellationToken)
    {
        var namespaces = new Stack<INamespaceSymbol>();
        namespaces.Push(root);
        while (namespaces.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var child in current.GetNamespaceMembers().OrderBy(symbol => symbol.Name, StringComparer.Ordinal))
                namespaces.Push(child);
            foreach (var type in current.GetTypeMembers().OrderBy(symbol => symbol.Name, StringComparer.Ordinal))
                foreach (var symbol in EnumerateType(type, cancellationToken))
                    yield return symbol;
        }
    }

    private static IEnumerable<ISymbol> EnumerateType(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        if (type.Locations.Any(location => location.IsInSource))
            yield return type;
        foreach (var member in type.GetMembers().OrderBy(symbol => symbol.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is INamedTypeSymbol nested)
            {
                foreach (var symbol in EnumerateType(nested, cancellationToken))
                    yield return symbol;
            }
            else if (member.Locations.Any(location => location.IsInSource)
                     && member is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)
            {
                yield return member;
            }
        }
    }

    private static SymbolCandidate? CreateCandidate(
        Project project,
        ISymbol symbol,
        string query,
        bool fuzzy,
        string sourceRevision)
    {
        var signature = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var documentationId = symbol.GetDocumentationCommentId() ?? signature;
        var projectIdentity = project.FilePath ?? project.Name;
        var symbolId = $"roslyn://{Uri.EscapeDataString(projectIdentity)}/revision/{sourceRevision}/symbol/{Uri.EscapeDataString(documentationId)}";
        var rank = Rank(symbolId, signature, symbol.Name, query, fuzzy);
        if (rank < 0)
            return null;

        var source = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        var arity = symbol switch
        {
            INamedTypeSymbol namedType => namedType.Arity,
            IMethodSymbol method => method.Arity,
            _ => 0
        };
        return new SymbolCandidate(
            symbolId,
            symbol.Kind.ToString(),
            symbol.Name,
            signature,
            signature,
            project.Name,
            symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            symbol.DeclaredAccessibility.ToString(),
            arity,
            source?.SourceTree?.FilePath,
            rank);
    }

    private static int Rank(string symbolId, string fqn, string name, string query, bool fuzzy)
    {
        if (string.Equals(symbolId, query, StringComparison.Ordinal))
            return 0;
        if (string.Equals(fqn, query, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (name.EndsWith(query, StringComparison.OrdinalIgnoreCase)
            || fqn.EndsWith(query, StringComparison.OrdinalIgnoreCase))
            return 3;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 4;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || fqn.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 5;
        return fuzzy && IsSubsequence(query, name) ? 6 : -1;
    }

    private static bool IsSubsequence(string query, string candidate)
    {
        var queryIndex = 0;
        foreach (var character in candidate)
        {
            if (queryIndex < query.Length
                && char.ToUpperInvariant(query[queryIndex]) == char.ToUpperInvariant(character))
                queryIndex++;
        }

        return queryIndex == query.Length;
    }

    private static bool Matches(string? filter, string actual) =>
        string.IsNullOrWhiteSpace(filter)
        || string.Equals(filter, actual, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "type" => "NamedType",
        "method" => "Method",
        "property" => "Property",
        "field" => "Field",
        "event" => "Event",
        _ => kind
    };

    private static bool TryGetIdentityRevision(string identity, out string revision)
    {
        const string marker = "/revision/";
        const string terminator = "/symbol/";
        revision = string.Empty;
        if (!identity.StartsWith("roslyn://", StringComparison.Ordinal))
            return false;

        var markerIndex = identity.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return false;
        var revisionStart = markerIndex + marker.Length;
        var revisionEnd = identity.IndexOf(terminator, revisionStart, StringComparison.Ordinal);
        if (revisionEnd <= revisionStart)
            return false;

        revision = identity[revisionStart..revisionEnd];
        return true;
    }
}
