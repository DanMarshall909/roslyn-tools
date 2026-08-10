using System.Text.RegularExpressions;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Services.GitHub;

/// <summary>
///     Ranks candidate GitHub issues against a new <c>rt feedback report</c> submission using
///     the shared <see cref="Bm25Core" />, so duplicate detection works without a new LLM
///     dependency. Category and command are weighted well above the free-text title/body,
///     because they are the mandatory, consistent fields every report carries (see
///     <see cref="RoslynTools.Feedback.FeedbackCategories" />) — a category/command match is a
///     far stronger duplicate signal than incidental word overlap.
/// </summary>
/// <remarks>
///     Category/command aren't sourced from GitHub labels (that would require every distinct
///     command name to exist as a pre-provisioned label, which does not scale). Instead
///     <c>FeedbackReportOperation</c> bakes them into every filed issue's title as
///     <c>[category] [command] title</c>, and <see cref="ParseTitlePrefix" /> reads them back
///     off existing issues here. Issues without the prefix (e.g. filed by hand before this
///     feature existed) simply score 0 on those two fields and fall back to title/body overlap.
/// </remarks>
internal static class IssueDuplicateScorer
{
    private const double CategoryWeight = 4.0;
    private const double CommandWeight = 3.5;
    private const double TitleWeight = 2.0;
    private const double BodyWeight = 0.5;

    private static readonly Regex TitlePrefix =
        new(@"^\[(?<category>[^\[\]]+)\](?:\s*\[(?<command>[^\[\]]+)\])?", RegexOptions.Compiled);

    internal static IReadOnlyList<ScoredIssue> Score(
        IReadOnlyList<GitHubIssueSummary> candidates,
        string category,
        string? command,
        string title,
        int top)
    {
        var query = string.Join(' ', new[] { category, command, title }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var results = Bm25Core.Score(
            candidates,
            issue =>
            {
                var (candidateCategory, candidateCommand) = ParseTitlePrefix(issue.Title);
                return (IReadOnlyList<Bm25Core.WeightedField>)
                [
                    new Bm25Core.WeightedField(candidateCategory, CategoryWeight),
                    new Bm25Core.WeightedField(candidateCommand, CommandWeight),
                    new Bm25Core.WeightedField(issue.Title, TitleWeight),
                    new Bm25Core.WeightedField(issue.Body ?? "", BodyWeight)
                ];
            },
            query,
            top);

        return results.Select(r => new ScoredIssue(r.Item, r.Score)).ToList();
    }

    private static (string Category, string Command) ParseTitlePrefix(string title)
    {
        var match = TitlePrefix.Match(title);
        if (!match.Success)
            return ("", "");
        return (match.Groups["category"].Value, match.Groups["command"].Success ? match.Groups["command"].Value : "");
    }
}

/// <summary>A single ranked result from <see cref="IssueDuplicateScorer.Score" />.</summary>
internal sealed record ScoredIssue(GitHubIssueSummary Issue, double Score);
