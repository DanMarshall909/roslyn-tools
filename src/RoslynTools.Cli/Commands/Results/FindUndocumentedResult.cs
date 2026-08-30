using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands.Results;

/// <summary>
/// Reports on undocumented symbols in the project, including their usage and location.
/// </summary>
internal sealed record UndocumentedSymbolResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "fqn")] public required string Fqn { get; init; }

    [OutputField(OutputLevel.Llm, "signature")]
    public required string Signature { get; init; }

    [OutputField(OutputLevel.Llm, "kind")] public required string Kind { get; init; }

    [OutputField(OutputLevel.Default, "filePath")]
    public required string FilePath { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{Kind,-12} {Fqn,-60} {FilePath}:{Line}");
    }
}

/// <summary>
/// Searches for undocumented symbols in the project and provides their details.
/// </summary>
internal sealed record FindUndocumentedResult : IRtOutput, IRtJsonOutput
{
    [OutputField(OutputLevel.Llm, "symbols")]
    public required IReadOnlyList<UndocumentedSymbolResult> Symbols { get; init; }

    public IReadOnlyList<SuggestedFix> SuggestedFixes { get; init; } = [];

    public void RenderJson(TextWriter writer)
    {
        var doc = new
        {
            symbols = Symbols.Select(s => new
            {
                fqn = s.Fqn,
                signature = s.Signature,
                kind = s.Kind,
                filePath = s.FilePath,
                line = s.Line
            }).ToArray(),
            suggestedFixes = SuggestedFixes.Select(f => f.AsJsonObject()).ToArray()
        };
        writer.Write(JsonSerializer.Serialize(doc, SymbolFormatter.DefaultJsonOptions));
    }

    public void RenderText(TextWriter writer)
    {
        for (var i = 0; i < Symbols.Count; i++)
        {
            Symbols[i].RenderText(writer);
            if (i < Symbols.Count - 1) writer.WriteLine();
        }

        if (SuggestedFixes.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine();
            foreach (var fix in SuggestedFixes)
            {
                fix.RenderText(writer);
                writer.WriteLine();
            }
        }
    }

    internal static FindUndocumentedResult From(IEnumerable<UndocumentedSymbol> symbols, string? solutionPath = null)
    {
        return new FindUndocumentedResult
        {
            Symbols = symbols.Select(s => new UndocumentedSymbolResult
            {
                Fqn = s.Fqn,
                Signature = s.Signature,
                Kind = s.Kind,
                FilePath = s.FilePath,
                Line = s.Line
            }).ToList(),
            SuggestedFixes = FixCatalog.ForUndocumented(solutionPath)
        };
    }

    internal static FindUndocumentedResult FromAnalysis(FindUndocumentedAnalysisResult result) => new()
    {
        Symbols = result.Symbols.Select(s => new UndocumentedSymbolResult
        {
            Fqn = s.Fqn,
            Signature = s.Signature,
            Kind = s.Kind,
            FilePath = s.FilePath,
            Line = s.Line
        }).ToList(),
        SuggestedFixes = result.SuggestedFixes
    };
}
