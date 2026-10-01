namespace RoslynTools.Services.Suggest;

/// <summary>An ordered, invokable route for one recognized user task.</summary>
internal sealed record SuggestWorkflow(IReadOnlyList<SuggestWorkflowStep> Steps);

/// <summary>One read or plan step with a CLI call and an MCP call when its required target is known.</summary>
internal sealed record SuggestWorkflowStep(
    string CliInvocation,
    string? McpTool,
    IReadOnlyDictionary<string, string>? McpArguments,
    string Reason,
    string StopCondition,
    string? McpUnavailableReason = null);
