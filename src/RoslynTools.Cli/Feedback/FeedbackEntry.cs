namespace RoslynTools.Feedback;

internal sealed class FeedbackEntry
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "user-feedback";
    public string Timestamp { get; set; } = "";
    public string? CommandLine { get; set; }
    public string Description { get; set; } = "";
    public string? SampleCode { get; set; }
    public string? ExpectedResult { get; set; }
    public string? ActualResult { get; set; }
    public string? StackTrace { get; set; }
    public string Status { get; set; } = "pending";
    public string? ProcessedAt { get; set; }
    public string? Resolution { get; set; }

    // Populated once `rt feedback report` files or links the entry on GitHub.
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
