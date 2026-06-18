using System.Text.Json.Serialization;
using RoslynTools.Core.Output;

namespace RoslynTools.Services.Results;

/// <summary>
/// A class representing the result of finding references for a symbol in a solution.
/// </summary>
internal sealed record ReferenceResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "filePath")]
    public required string FilePath { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    [OutputField(OutputLevel.Default, "lineText")]
    public required string LineText { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{FilePath}({Line}): {LineText}");
    }
}

/// <summary>
/// A method that finds references for a symbol in a solution.
/// </summary>
internal sealed record FindRefsResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "symbolName")]
    public required string SymbolName { get; init; }

    [OutputField(OutputLevel.Llm, "fullyQualifiedName")]
    public required string FullyQualifiedName { get; init; }

    [OutputField(OutputLevel.Llm, "references")]
    public required IReadOnlyList<ReferenceResult> References { get; init; }

    [JsonPropertyName("_suggestions")]
    public IReadOnlyList<string>? Suggestions { get; init; }

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
