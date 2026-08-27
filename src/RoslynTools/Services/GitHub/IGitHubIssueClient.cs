namespace RoslynTools.Services.GitHub;

/// <summary>One GitHub issue returned by the <c>gh</c> CLI.</summary>
internal sealed record GitHubIssueSummary(
    int Number,
    string Title,
    string Url,
    string? Body,
    string State);

/// <summary>
///     Narrow, testable boundary around GitHub issue operations. Authentication stays in
///     <c>gh</c>; rt never accepts or stores a GitHub token.
/// </summary>
internal interface IGitHubIssueClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitHubIssueSummary>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default);

    Task<GitHubIssueSummary> GetIssueAsync(
        int number,
        CancellationToken cancellationToken = default);

    Task<GitHubIssueSummary> CreateIssueAsync(
        string title,
        string body,
        CancellationToken cancellationToken = default);
}
