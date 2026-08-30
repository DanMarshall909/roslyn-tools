using RoslynTools.Services.Suggest;

namespace RoslynTools.Services.Results;

internal sealed record UndocumentedSymbolResult(
    string Fqn,
    string Signature,
    string Kind,
    string FilePath,
    int Line);

internal sealed record FindUndocumentedAnalysisResult(
    IReadOnlyList<UndocumentedSymbolResult> Symbols,
    IReadOnlyList<SuggestedFix> SuggestedFixes);
