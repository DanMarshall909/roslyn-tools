namespace RoslynTools.Services.GitHub;

/// <summary>One GitHub issue, as returned by <c>gh issue list</c>/<c>view</c>/<c>create</c>.</summary>
internal sealed record GitHubIssueSummary(int Number, string Title, string Url, string? Body, string State);

/// <summary>
///     Narrow abstraction over the <c>gh</c> CLI, scoped to the process's current working
///     directory. Exists so <see cref="RoslynTools.Feedback.FeedbackReportOperation" /> can be
///     unit-tested with a fake — no real <c>gh</c> install or network access required. The one
///     real implementation, <see cref="GitHubIssueClient" />, shells <c>gh</c> via
///     <see cref="System.Diagnostics.Process" />, mirroring
///     <see cref="RoslynTools.Services.SemanticDiff.Git.IGitClient" />.
///     <para>
///         Authentication is entirely delegated to <c>gh</c>'s own credential store
///         (<c>gh auth login</c>) — this client never sees, stores, or accepts a token. That is
///         also why creating an issue happens under the reporter's own GitHub identity, which is
///         what makes this safe to ship to end users on a public repository: filing an issue
///         needs no special write access.
///     </para>
/// </summary>
internal interface IGitHubIssueClient
{
    /// <summary>True when <c>gh</c> is on PATH and authenticated (<c>gh auth status</c>).</summary>
    bool IsAvailable();

    /// <summary>
    ///     <c>gh issue list --search &lt;query&gt; --state all --limit &lt;limit&gt;</c> against the
    ///     repository resolved from the current working directory.
    /// </summary>
    IReadOnlyList<GitHubIssueSummary> Search(string query, int limit);

    /// <summary><c>gh issue view &lt;number&gt;</c> — used by <c>--attach-to</c>.</summary>
    GitHubIssueSummary GetIssue(int number);

    /// <summary><c>gh issue create --title ... --body-file ...</c>. Returns the created issue.</summary>
    GitHubIssueSummary CreateIssue(string title, string body);
}
