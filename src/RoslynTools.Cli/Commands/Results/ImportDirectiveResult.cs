using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands.Results;

/// <summary>
/// Represents the result of analyzing an import directive entry.
/// </summary>
internal sealed record ImportDirectiveEntryResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "kind")] public required string Kind { get; init; }

    [OutputField(OutputLevel.Llm, "name")] public required string Name { get; init; }

    [OutputField(OutputLevel.Default, "filePath")]
    public required string FilePath { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    [OutputField(OutputLevel.Default, "alias")]
    public string? Alias { get; init; }

    public void RenderText(TextWriter writer)
    {
        var alias = Alias is null ? string.Empty : $" alias={Alias}";
        writer.Write($"{Kind,-13} {Name,-45} {FilePath}:{Line}{alias}");
    }
}

/// <summary>
/// Describes the outcome of an import directive analysis, including any issues found.
/// </summary>
internal sealed record ImportDirectiveResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "imports")]
    public required IReadOnlyList<ImportDirectiveEntryResult> Imports { get; init; }

    public void RenderText(TextWriter writer)
    {
        for (var i = 0; i < Imports.Count; i++)
        {
            Imports[i].RenderText(writer);
            if (i < Imports.Count - 1)
                writer.WriteLine();
        }
    }

    internal static ImportDirectiveResult FromImports(IEnumerable<ListImportsEntry> imports)
    {
        return new ImportDirectiveResult
        {
            Imports = imports.Select(i => new ImportDirectiveEntryResult
            {
                Kind = i.Kind,
                Name = i.Name,
                FilePath = i.FilePath,
                Line = i.Line,
                Alias = i.Alias
            }).ToList()
        };
    }
}
