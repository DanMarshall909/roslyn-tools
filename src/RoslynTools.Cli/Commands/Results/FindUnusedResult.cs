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

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{Kind,-10} {Name,-40} {FilePath}:{Line}");
    }
}

/// <summary>Top-level result returned by find-unused.</summary>
internal sealed record FindUnusedResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "symbols")]
    public required IReadOnlyList<UnusedSymbolResult> Symbols { get; init; }

    public void RenderText(TextWriter writer)
    {
        for (var i = 0; i < Symbols.Count; i++)
        {
            Symbols[i].RenderText(writer);
            if (i < Symbols.Count - 1)
                writer.WriteLine();
        }
    }

    internal static FindUnusedResult FromSymbols(IEnumerable<UnusedSymbol> symbols)
    {
        return new FindUnusedResult
        {
            Symbols = symbols.Select(s => new UnusedSymbolResult
            {
                Kind = s.Kind,
                Name = s.Name,
                FilePath = s.FilePath ?? string.Empty,
                Line = s.Line
            }).ToList()
        };
    }
}
