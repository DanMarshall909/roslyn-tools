using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands.Results;

/// <summary>Per-symbol entry in a find-unused result.</summary>
internal sealed record UnusedSymbolResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "kind")] public required string Kind { get; init; }

    [OutputField(OutputLevel.Llm, "name")] public required string Name { get; init; }

    [OutputField(OutputLevel.Default, "filePath")]
    public required string FilePath { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    [OutputField(OutputLevel.Llm, "confidence")]
    public required string Confidence { get; init; }

    [OutputField(OutputLevel.Llm, "reason")]
    public required string Reason { get; init; }

    public IReadOnlyList<SuggestedFix> SuggestedFixes { get; init; } = [];

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{Kind,-10} {Name,-40} {FilePath}:{Line} [{Confidence}] {Reason}");
        foreach (var fix in SuggestedFixes)
        {
            writer.WriteLine();
            fix.RenderText(writer);
        }
    }

}

/// <summary>Top-level result returned by find-unused.</summary>
internal sealed record FindUnusedResult : IRtOutput, IRtJsonOutput
{
    [OutputField(OutputLevel.Llm, "symbols")]
    public required IReadOnlyList<UnusedSymbolResult> Symbols { get; init; }

    /// <summary>
    ///     Where the run spent its time and how many reference searches it issued. Present only when
    ///     the caller asked; absent from the output otherwise, so a profiled run and an ordinary one
    ///     cannot be confused after the fact.
    /// </summary>
    public FindUnusedProfile? Profile { get; init; }

    public void RenderJson(TextWriter writer)
    {
        var doc = new
        {
            symbols = Symbols.Select(s => new
            {
                kind = s.Kind,
                name = s.Name,
                filePath = s.FilePath,
                line = s.Line,
                confidence = s.Confidence,
                reason = s.Reason,
                suggestedFixes = s.SuggestedFixes.Select(f => f.AsJsonObject()).ToArray()
            }).ToArray(),
            profile = Profile is null
                ? null
                : new
                {
                    totalMs = Profile.TotalMs,
                    candidateDiscoveryMs = Profile.CandidateDiscoveryMs,
                    reflectionScanMs = Profile.ReflectionScanMs,
                    referenceSearchMs = Profile.ReferenceSearchMs,
                    documents = Profile.Documents,
                    documentsSkipped = Profile.DocumentsSkipped,
                    typeCandidates = Profile.TypeCandidates,
                    methodCandidates = Profile.MethodCandidates,
                    uniqueCandidates = Profile.UniqueCandidates,
                    referenceQueries = Profile.ReferenceQueries,
                    extensionMethodQueries = Profile.ExtensionMethodQueries,
                    searchedDocuments = Profile.SearchedDocuments
                }
        };
        writer.Write(JsonSerializer.Serialize(doc, SymbolFormatter.DefaultJsonOptions));
    }

    public void RenderText(TextWriter writer)
    {
        for (var i = 0; i < Symbols.Count; i++)
        {
            Symbols[i].RenderText(writer);
            if (i < Symbols.Count - 1)
                writer.WriteLine();
        }

        if (Profile is null)
            return;

        if (Symbols.Count > 0)
            writer.WriteLine();
        writer.WriteLine();
        writer.WriteLine(
            $"profile  {Profile.TotalMs}ms total = {Profile.CandidateDiscoveryMs}ms discovery "
            + $"+ {Profile.ReflectionScanMs}ms reflection scan "
            + $"+ {Profile.ReferenceSearchMs}ms reference search");
        writer.Write(
            $"profile  {Profile.Documents} documents ({Profile.DocumentsSkipped} skipped), "
            + $"{Profile.TypeCandidates} type + {Profile.MethodCandidates} method declarations "
            + $"-> {Profile.UniqueCandidates} candidates, {Profile.ReferenceQueries} reference "
            + $"searches ({Profile.ExtensionMethodQueries} for extension methods) "
            + $"over {Profile.SearchedDocuments} documents");
    }

    internal static FindUnusedResult FromSymbols(IEnumerable<UnusedSymbol> symbols, string? solutionPath = null)
    {
        return new FindUnusedResult
        {
            Symbols = symbols.Select(s => new UnusedSymbolResult
            {
                Kind = s.Kind,
                Name = s.Name,
                FilePath = s.FilePath ?? string.Empty,
                Line = s.Line,
                Confidence = s.Confidence,
                Reason = s.Reason,
                SuggestedFixes = s.Confidence.Equals(UnusedConfidence.High, StringComparison.OrdinalIgnoreCase)
                    ? FixCatalog.ForUnusedSymbol(s.Kind, s.Name, solutionPath)
                    : []
            }).ToList()
        };
    }

    internal static FindUnusedResult FromAnalysis(FindUnusedAnalysisResult result) => new()
    {
        Profile = result.Profile,
        Symbols = result.Symbols.Select(s => new UnusedSymbolResult
        {
            Kind = s.Kind,
            Name = s.Name,
            FilePath = s.FilePath,
            Line = s.Line,
            Confidence = s.Confidence,
            Reason = s.Reason,
            SuggestedFixes = s.SuggestedFixes
        }).ToList()
    };
}
