using RoslynTools.Services.Results;

namespace RoslynTools.Services;

/// <summary>
/// Indicates the presence of a code smell item, including its type and location.
/// </summary>
internal sealed record SmellItemResult
{
    public required string SmellType { get; init; }

    public required string TypeName { get; init; }

    public required string? MemberName { get; init; }

    public required string FilePath { get; init; }

    public required int Line { get; init; }

    public required string Detail { get; init; }

    public IReadOnlyList<SuggestedFix> SuggestedFixes { get; init; } = [];
}

/// <summary>
/// Lists all code smell items found in the project.
/// </summary>
internal sealed record ListSmellsResult
{
    public required IReadOnlyList<SmellItemResult> Smells { get; init; }
}
