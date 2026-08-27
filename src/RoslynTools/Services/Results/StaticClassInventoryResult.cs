using RoslynTools.Models;

namespace RoslynTools.Services.Results;

internal sealed record StaticClassInventoryAnalysisResult(
    int TotalStaticClasses,
    IReadOnlyList<StaticClassEntry> Classes);
