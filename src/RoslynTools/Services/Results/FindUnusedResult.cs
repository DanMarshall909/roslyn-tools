using RoslynTools.Services.Suggest;

namespace RoslynTools.Services.Results;

internal sealed record UnusedSymbolResult(
    string Kind,
    string Name,
    string FilePath,
    int Line,
    string Confidence,
    string Reason,
    IReadOnlyList<SuggestedFix> SuggestedFixes);

internal sealed record FindUnusedAnalysisResult(
    IReadOnlyList<UnusedSymbolResult> Symbols,
    FindUnusedProfile? Profile = null);
