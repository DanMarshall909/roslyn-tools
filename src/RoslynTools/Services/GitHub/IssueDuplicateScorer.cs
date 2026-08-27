using RoslynTools.Services.Suggest;

namespace RoslynTools.Services.GitHub;

/// <summary>Ranks existing issues by title and body overlap with local feedback.</summary>
internal static class IssueDuplicateScorer
{
    private const double TitleWeight = 2.0;
    private const double BodyWeight = 0.5;

    internal static IReadOnlyList<ScoredIssue> Score(
        IReadOnlyList<GitHubIssueSummary> candidates,
        string description,
        string? commandLine,
        int top)
    {
        var query = string.Join(' ', new[] { description, commandLine }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return Bm25Core.Score(
                candidates,
                issue =>
                    (IReadOnlyList<Bm25Core.WeightedField>)
                    [
                        new(issue.Title, TitleWeight),
                        new(issue.Body ?? "", BodyWeight)
                    ],
                query,
                top)
            .Select(result => new ScoredIssue(result.Item, result.Score))
            .ToList();
    }
}

internal sealed record ScoredIssue(GitHubIssueSummary Issue, double Score);
