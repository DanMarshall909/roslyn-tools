namespace RoslynTools.Services.Results;

/// <summary>
/// Describes a follow-on action suggested by an analysis result.
/// </summary>
public sealed record SuggestedFix
{
    public required string Tool { get; init; }
    public required string Ref { get; init; }
    public required string Description { get; init; }
    public string? Command { get; init; }
    public IReadOnlyList<SuggestedFixArgument> Arguments { get; init; } = [];
}

/// <summary>One typed token in a suggested command invocation.</summary>
public sealed record SuggestedFixArgument(string Value, bool Quote = false);
