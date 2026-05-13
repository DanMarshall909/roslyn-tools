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
}
