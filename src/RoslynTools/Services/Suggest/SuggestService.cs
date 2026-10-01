using System.Text.RegularExpressions;
using RoslynTools.Core;

namespace RoslynTools.Services.Suggest;

/// <summary>
///     Host-agnostic command-suggestion service. CLI's <c>SuggestCommand</c>
///     and MCP's <c>SuggestHandler</c> are thin adapters around this — both
///     resolve it from DI and call <see cref="Suggest" /> with a parsed
///     <see cref="SuggestRequest" />, then format the
///     <see cref="SuggestResponse" /> for their respective surfaces.
/// </summary>
/// <remarks>
///     This is the reference implementation of the Core-service + thin-host-
///     adapters pattern documented in CLAUDE.md. Nothing in this file depends
///     on <c>System.CommandLine</c>, <c>Console</c>, or any host's catalog
///     discovery mechanism — it talks only to the injected
///     <see cref="ICommandCatalog" />.
/// </remarks>
internal sealed class SuggestService : IRtService
{
    private readonly ICommandCatalog _catalog;

    public SuggestService(ICommandCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>
    ///     Rank the catalog against the query and return up to
    ///     <see cref="SuggestRequest.Top" /> hits. The top result's see-also
    ///     neighbours (once that field lands on <see cref="RtCommandInfo" />)
    ///     are attached as <see cref="SuggestResponse.Related" />.
    /// </summary>
    public SuggestResponse Suggest(SuggestRequest request)
    {
        if (UnavailablePlanCapability(request.Query) is { } missingCapability)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, missingCapability);

        var scored = Bm25Scorer.Score(
            _catalog.All,
            request.Query,
            request.Top,
            Synonyms.NameSynonyms);

        var results = scored.Select(ToResult).ToList();
        var related = BuildRelated(results, _catalog.All);

        return new SuggestResponse(
            results,
            related,
            results.Count == 0 ? SuggestOutcome.NoMatch : SuggestOutcome.Recommendations);
    }

    private static string? UnavailablePlanCapability(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var namedCapability = query.Trim();
        if (namedCapability.Equals("verify-plan", StringComparison.OrdinalIgnoreCase))
            return "verify-plan";
        if (namedCapability.Equals("prepare-plan", StringComparison.OrdinalIgnoreCase))
            return "prepare-plan";

        // A symbol or source file named Plan is not a request for a change-plan artifact.
        var task = Regex.Replace(query,
            @"\b(?:named|called|class|struct|interface|type|method|file)\s+Plan\b|\bPlan\s+(?:class|struct|interface|type|method|file)\b|\bPlan\.(?:cs|csproj)\b",
            " ", options);
        if (!Regex.IsMatch(task, @"\bplans?\b", options))
            return null;
        if (Regex.IsMatch(task,
                @"\b(?:verify|validate|check|review)\s+(?:(?:a|an|the|my|our|existing|prepared|reviewed|immutable|revision-bound|change|stored|saved|proposed|pending|rename|signature|refactor|refactoring)\s+){0,6}plans?\b",
                options)
            || Regex.IsMatch(task, @"\bplans?\s+(?:verification|validation)\b", options)
            || Regex.IsMatch(task,
                @"\bplans?\s+(?:and|then)\s+(?:verify|validate|check|review)\s+(?:it|them)\b",
                options))
            return "verify-plan";
        return Regex.IsMatch(task,
                   @"\b(?:prepare|preparing|create|generate|produce|build)\b(?:\s+[\w-]+){0,8}\s+plans?\b",
                   options)
               || Regex.IsMatch(task,
                   @"\b(?:immutable|revision-bound)\b(?:\s+[\w-]+){0,8}\s+plans?\b",
                   options)
            ? "prepare-plan"
            : null;
    }

    private static SuggestResult ToResult(ScoredCommand s) => new(
        Name: s.Command.Name,
        Score: s.Score,
        Category: s.Command.Category,
        WhenToUse: s.Command.WhenToUse,
        PrimaryExample: s.Command.Examples.Length > 0 ? s.Command.Examples[0] : "",
        AllExamples: s.Command.Examples);

    private static List<SuggestResult> BuildRelated(
        List<SuggestResult> results,
        IReadOnlyList<RtCommandInfo> catalog)
    {
        // See-also lookup. Activates once RtCommandInfo carries SeeAlso; until
        // then this is a no-op so the service ships before the schema change.
        if (results.Count == 0)
            return [];
        var top = catalog.FirstOrDefault(c => c.Name == results[0].Name);
        if (top is null || top.SeeAlso is null || top.SeeAlso.Length == 0)
            return [];
        var lookup = catalog.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var related = new List<SuggestResult>(top.SeeAlso.Length);
        foreach (var name in top.SeeAlso)
        {
            if (!lookup.TryGetValue(name, out var info))
                continue;
            related.Add(new SuggestResult(
                Name: info.Name,
                Score: 0,
                Category: info.Category,
                WhenToUse: info.WhenToUse,
                PrimaryExample: info.Examples.Length > 0 ? info.Examples[0] : "",
                AllExamples: info.Examples));
        }
        return related;
    }
}
