using RoslynTools.Core.Output;

namespace RoslynTools.Services.Results;

/// <summary>
/// A class representing the result of finding callers for a symbol in a solution.
/// </summary>
internal sealed record CallerResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "filePath")]
    public required string FilePath { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    [OutputField(OutputLevel.Default, "containingMethod")]
    public required string ContainingMethod { get; init; }

    [OutputField(OutputLevel.Default, "sourceLine")]
    public required string SourceLine { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{FilePath}:{Line}: {ContainingMethod}: {SourceLine}");
    }
}

/// <summary>
/// A method that finds callers for a symbol in a solution.
/// </summary>
internal sealed record FindCallersResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "symbolName")]
    public required string SymbolName { get; init; }

    [OutputField(OutputLevel.Llm, "callers")]
    public required IReadOnlyList<CallerResult> Callers { get; init; }

    public void RenderText(TextWriter writer)
    {
        foreach (var caller in Callers)
        {
            caller.RenderText(writer);
            writer.WriteLine();
        }
    }
}
