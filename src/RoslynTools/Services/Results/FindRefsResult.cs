using RoslynTools.Core.Output;

namespace RoslynTools.Services.Results;

internal sealed record ReferenceResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "filePath")]
    public required string FilePath { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    [OutputField(OutputLevel.Default, "lineText")]
    public required string LineText { get; init; }

    public void RenderText(TextWriter writer) =>
        writer.Write($"{FilePath}({Line}): {LineText}");
}

internal sealed record FindRefsResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "symbolName")]
    public required string SymbolName { get; init; }

    [OutputField(OutputLevel.Llm, "references")]
    public required IReadOnlyList<ReferenceResult> References { get; init; }

    public void RenderText(TextWriter writer)
    {
        foreach (var reference in References)
        {
            reference.RenderText(writer);
            writer.WriteLine();
        }

        writer.WriteLine();
        writer.WriteLine($"Found {References.Count} reference(s) to '{SymbolName}'.");
    }
}
