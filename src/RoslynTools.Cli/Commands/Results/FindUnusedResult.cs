using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands.Results;

/// <summary>Per-symbol entry in a find-unused result.</summary>
internal sealed record UnusedSymbolResult : IRtOutput
{
    // Kind and Name are Llm-level so the LLM renderer writes them;
    // FilePath and Line are Default so all renderers include them.

    [OutputField(OutputLevel.Llm, "kind")] public required string Kind { get; init; }

    [OutputField(OutputLevel.Llm, "name")] public required string Name { get; init; }

    [OutputField(OutputLevel.Default, "filePath")]
    public required string FilePath { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    public IReadOnlyList<SuggestedFix> SuggestedFixes { get; init; } = [];

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{Kind,-10} {Name,-40} {FilePath}:{Line}");
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
                suggestedFixes = s.SuggestedFixes.Select(f => f.AsJsonObject()).ToArray()
            }).ToArray()
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
                SuggestedFixes = FixCatalog.ForUnusedSymbol(s.Kind, s.Name, solutionPath)
            }).ToList()
        };
    }
}
