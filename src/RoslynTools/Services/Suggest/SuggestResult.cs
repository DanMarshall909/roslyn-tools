namespace RoslynTools.Services.Suggest;

/// <summary>
///     A single ranked suggestion. Carries everything either host needs to
///     render — CLI picks <see cref="PrimaryExample" /> for terse output, MCP
///     surfaces <see cref="AllExamples" /> in the structured JSON return.
/// </summary>
internal sealed record SuggestResult(
    string Name,
    double Score,
    string Category,
    string WhenToUse,
    string PrimaryExample,
    IReadOnlyList<string> AllExamples);
