using System.Text.RegularExpressions;
using RoslynTools.Core;
using RoslynTools.Services.SemanticDiff.Git;

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
    private readonly bool _workingTreeReadsAvailable;

    public SuggestService(ICommandCatalog catalog) : this(catalog, WorkingTreeReadCapability.IsAvailable)
    {
    }

    internal SuggestService(ICommandCatalog catalog, bool workingTreeReadsAvailable)
    {
        _catalog = catalog;
        _workingTreeReadsAvailable = workingTreeReadsAvailable;
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

        if (ImplementationSearchSymbol(request.Query) is { } symbol)
            return SuggestImplementations(symbol, request.Solution);

        if (IsWorkingTreeReview(request.Query))
            return SuggestWorkingTreeReview(request.Query, request.Solution);

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

    private SuggestResponse SuggestWorkingTreeReview(string query, string? solution)
    {
        const string capability = "semantic-diff-working-tree";
        if (!_workingTreeReadsAvailable)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, capability,
                UnsupportedReason: "Working-tree C# semantic diff requires Linux x64; compare two Git refs or two .cs files instead.");
        if (Regex.IsMatch(query, @"\bunstaged\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "semantic-diff-unstaged",
                UnsupportedReason: "The current semantic diff compares HEAD with the working tree and includes staged changes; an unstaged-only comparison is unavailable.");
        if (HasExplicitWorkingTreeBaseline(query))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "semantic-diff-working-tree-baseline",
                UnsupportedReason: "This suggestion route cannot safely infer an explicit baseline from the request. Run semantic-diff --from <ref> directly, or compare two Git refs.");
        if (_catalog.All.All(info => info.Name != "semantic-diff"))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, capability,
                UnsupportedReason: "The semantic-diff command is unavailable in this command catalogue.");

        var hasMcpTarget = !string.IsNullOrWhiteSpace(solution) && Path.IsPathFullyQualified(solution);
        var directory = hasMcpTarget ? Path.GetDirectoryName(solution!) : null;
        IReadOnlyDictionary<string, string>? arguments = directory is not null
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["directory"] = directory }
            : null;
        var cli = "rt analyze semantic-diff"
                  + (directory is null ? "" : $" --directory {QuoteCliArgument(directory)}")
                  + " --json";
        var step = new SuggestWorkflowStep(
            cli,
            arguments is null ? null : "semantic_diff",
            arguments,
            "Review surfaced C# declaration changes between HEAD and current files, including staged changes.",
            "Stop on a Git or content-read error; an empty result covers surfaced declarations only and does not prove unchanged behavior.",
            arguments is null ? "MCP requires an absolute solution path; call suggest_command again with one." : null);
        return new SuggestResponse([], [], SuggestOutcome.Recommendations,
            Workflow: new SuggestWorkflow([step]));
    }

    private static bool IsWorkingTreeReview(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        return Regex.IsMatch(query, @"\b(?:working[ -]tree|uncommitted|unstaged)\b", options)
               && Regex.IsMatch(query, @"(?:C#|\bCSharp\b)", options)
               && Regex.IsMatch(query, @"\b(?:review|inspect|analy[sz]e|compare|diff)\b", options)
               && !Regex.IsMatch(query, @"^\s*(?:please\s+)?(?:apply|refactor|edit|rewrite|modify|commit|stage|rename)\b", options);
    }

    private static bool HasExplicitWorkingTreeBaseline(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        const string comparator = @"(?:\b(?:against|since|from|versus|compared\s+(?:with|to)|relative\s+to)\b|\bvs\b\.?)";
        foreach (Match comparative in Regex.Matches(query, comparator, options))
        {
            var tail = query[(comparative.Index + comparative.Length)..];
            if (comparative.Value.Equals("from", StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(tail,
                    @"^\s+(?:(?:my|the|our|your|this)\s+)?working[ -]tree\b", options))
                continue;
            var target = Regex.Match(tail,
                @"^\s+(?<ref>[A-Za-z0-9][A-Za-z0-9._~^/-]*)", options);
            if (!target.Success
                || !target.Groups["ref"].Value.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private SuggestResponse SuggestImplementations(string symbol, string? solution)
    {
        var command = _catalog.All.FirstOrDefault(info => info.Name == "relationships");
        if (command is null)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "relationships-implementations");

        var cli = $"rt inspect relationships implementations {symbol}"
                  + (string.IsNullOrWhiteSpace(solution) ? "" : $" --solution {QuoteCliArgument(solution)}")
                  + " --json";
        var hasMcpSolution = !string.IsNullOrWhiteSpace(solution)
                             && Path.IsPathFullyQualified(solution);
        IReadOnlyDictionary<string, string>? arguments = hasMcpSolution
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["symbol"] = symbol,
                ["mode"] = "Implementations",
                ["solution"] = solution!
            }
            : null;

        var step = new SuggestWorkflowStep(
            cli,
            hasMcpSolution ? "relationships" : null,
            arguments,
            "Read semantic implementations of the named type before planning a change.",
            "Inspect outcome and truncation before treating results as complete; Partial or truncated results cannot prove every implementation.",
            hasMcpSolution ? null : "MCP requires an absolute solution path; call suggest_command again with one.");
        return new SuggestResponse([], [], SuggestOutcome.Recommendations,
            Workflow: new SuggestWorkflow([step]));
    }

    private static string? ImplementationSearchSymbol(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var match = Regex.Match(query,
            @"\b(?:find|show|list)\s+(?:(?:all|every|the)\s+)?(?:implementations?|implementers?)\s+of\s+(?:(?:the|an?)\s+)?(?:interface\s+|type\s+)?(?<symbol>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b",
            options);
        if (!match.Success)
            return null;
        var next = match.Index + match.Length;
        if (next < query.Length && query[next] is '.' or '<' or '[' or ':' or '?')
            return null;
        if (Regex.IsMatch(query[next..],
                @"^\s*(?:,|\band\b|\bor\b)\s+[A-Za-z_][A-Za-z0-9_.]*", options))
            return null;
        var symbol = match.Groups["symbol"].Value;
        var leafName = symbol[(symbol.LastIndexOf('.') + 1)..];
        return char.IsUpper(leafName[0]) ? symbol : null;
    }

    private static string QuoteCliArgument(string value)
    {
        var safeCharacters = OperatingSystem.IsWindows()
            ? @"^[A-Za-z0-9_./:\-]+$"
            : @"^[A-Za-z0-9_./-]+$";
        if (Regex.IsMatch(value, safeCharacters, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            return value;

        return OperatingSystem.IsWindows()
            ? $"'{value.Replace("'", "''", StringComparison.Ordinal)}'"
            : $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
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
