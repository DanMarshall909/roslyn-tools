using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands.Results;

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

internal sealed record FindUndocumentedResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "symbols")]
    public required IReadOnlyList<UndocumentedSymbolResult> Symbols { get; init; }

    public void RenderText(TextWriter writer)
    {
        for (var i = 0; i < Symbols.Count; i++)
        {
            Symbols[i].RenderText(writer);
            if (i < Symbols.Count - 1) writer.WriteLine();
        }
    }

    internal static FindUndocumentedResult From(IEnumerable<UndocumentedSymbol> symbols)
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
            }).ToList()
        };
    }
}
