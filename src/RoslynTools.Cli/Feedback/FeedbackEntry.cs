using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynTools.Feedback;

internal sealed class FeedbackEntry
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "user-feedback";
    public string Timestamp { get; set; } = "";

    // Structured, mandatory-at-capture fields (see FeedbackCategory). Consistent
    // Title/Category/Command across every report is what makes GitHub-issue duplicate
    // detection in `rt feedback report` viable without an LLM call — see
    // RoslynTools.Services.GitHub.IssueDuplicateScorer.
    public string Title { get; set; } = "";
    public FeedbackCategory Category { get; set; }
    public string? Command { get; set; }

    public string? CommandLine { get; set; }
    public string Description { get; set; } = "";
    public string? SampleCode { get; set; }
    public string? ExpectedResult { get; set; }
    public string? ActualResult { get; set; }
    public string? StackTrace { get; set; }
    public string Status { get; set; } = "pending";
    public string? ProcessedAt { get; set; }
    public string? Resolution { get; set; }

    // Populated once `rt feedback report` files or links this entry on GitHub.
    public int? GitHubIssueNumber { get; set; }
    public string? GitHubIssueUrl { get; set; }

    // Diagnostic context — populated for exception entries to support fix-it triage.
    public string? Cwd { get; set; }
    public string? RtVersion { get; set; }
    public string? DotnetVersion { get; set; }
    public string? OsPlatform { get; set; }
    public string? SolutionPath { get; set; }
    public List<string>? InnerExceptions { get; set; }
    public Dictionary<string, string?>? ParsedOptions { get; set; }
}

/// <summary>
///     The small, fixed taxonomy every <c>rt feedback add</c> report is classified under. A
///     real enum, not free text, so an invalid category is a parse-time failure (CLI arg
///     parsing or MCP JSON deserialization) rather than a runtime string comparison. Kept
///     intentionally small: a broader set defeats the point of a mandatory category — a
///     strong duplicate-detection signal, see
///     <see cref="RoslynTools.Services.GitHub.IssueDuplicateScorer" />.
/// </summary>
[JsonConverter(typeof(FeedbackCategoryJsonConverter))]
internal enum FeedbackCategory
{
    Bug,
    Feature,
    Question,
    Other
}

/// <summary>Serializes <see cref="FeedbackCategory" /> as its lower-case name (e.g. "bug").</summary>
internal sealed class FeedbackCategoryJsonConverter : JsonStringEnumConverter<FeedbackCategory>
{
    public FeedbackCategoryJsonConverter() : base(JsonNamingPolicy.CamelCase)
    {
    }
}

internal static class FeedbackCategories
{
    /// <summary>
    ///     Display names for CLI help text and <c>--category</c> validation, and the single
    ///     source of truth <see cref="ToText" /> and the MCP schema builder both defer to —
    ///     <see cref="JsonNamingPolicy.CamelCase" /> is also what
    ///     <see cref="FeedbackCategoryJsonConverter" /> serializes with, so this always matches
    ///     the real wire format even if a future category name is more than one word (where
    ///     naively lower-casing the whole name would silently diverge from camelCase).
    /// </summary>
    internal static readonly string[] AllNames =
        [.. Enum.GetNames<FeedbackCategory>().Select(JsonNamingPolicy.CamelCase.ConvertName)];

    internal static string ToText(this FeedbackCategory category) =>
        JsonNamingPolicy.CamelCase.ConvertName(category.ToString());
}
