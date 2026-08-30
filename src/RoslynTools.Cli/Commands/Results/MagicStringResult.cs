using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands.Results;

/// <summary>
/// Tracks the occurrence of magic strings in the project, including their file and line locations.
/// </summary>
internal sealed record MagicStringOccurrenceResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "filePath")]
    public required string FilePath { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    [OutputField(OutputLevel.Default, "typeName")]
    public string? TypeName { get; init; }

    [OutputField(OutputLevel.Default, "memberName")]
    public string? MemberName { get; init; }

    public void RenderText(TextWriter writer)
    {
        var owner = TypeName is null ? string.Empty : $" {TypeName}.{MemberName}";
        writer.Write($"{FilePath}:{Line}{owner}");
    }
}

/// <summary>
/// Identifies magic string groups in the project, highlighting their usage patterns.
/// </summary>
internal sealed record MagicStringGroupResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "value")]
    public required string Value { get; init; }

    [OutputField(OutputLevel.Default, "count")]
    public required int Count { get; init; }

    [OutputField(OutputLevel.Default, "occurrences")]
    public required IReadOnlyList<MagicStringOccurrenceResult> Occurrences { get; init; }

    public IReadOnlyList<SuggestedFix> SuggestedFixes { get; init; } = [];

    public void RenderText(TextWriter writer)
    {
        writer.WriteLine($"{Count,3}  \"{Value}\"");
        foreach (var occurrence in Occurrences)
        {
            writer.Write("     ");
            occurrence.RenderText(writer);
            writer.WriteLine();
        }

        foreach (var fix in SuggestedFixes)
        {
            fix.RenderText(writer);
            writer.WriteLine();
        }
    }
}

/// <summary>
/// A class representing the result of a magic string command.
/// </summary>
internal sealed record MagicStringResult : IRtOutput, IRtJsonOutput
{
    [OutputField(OutputLevel.Llm, "groups")]
    public required IReadOnlyList<MagicStringGroupResult> Groups { get; init; }

    public void RenderJson(TextWriter writer)
    {
        var doc = new
        {
            groups = Groups.Select(g => new
            {
                value = g.Value,
                count = g.Count,
                occurrences = g.Occurrences.Select(o => new
                {
                    filePath = o.FilePath,
                    line = o.Line,
                    typeName = o.TypeName,
                    memberName = o.MemberName
                }).ToArray(),
                suggestedFixes = g.SuggestedFixes.Select(f => f.AsJsonObject()).ToArray()
            }).ToArray()
        };
        writer.Write(JsonSerializer.Serialize(doc, SymbolFormatter.DefaultJsonOptions));
    }

    public void RenderText(TextWriter writer)
    {
        for (var i = 0; i < Groups.Count; i++)
        {
            Groups[i].RenderText(writer);
            if (i < Groups.Count - 1)
                writer.WriteLine();
        }
    }

    internal static MagicStringResult FromGroups(IEnumerable<MagicStringGroup> groups, string? solutionPath = null)
    {
        return new MagicStringResult
        {
            Groups = groups.Select(g =>
            {
                var firstOccurrence = g.Occurrences.Count > 0 ? g.Occurrences[0] : null;
                return new MagicStringGroupResult
                {
                    Value = g.Value,
                    Count = g.Count,
                    Occurrences = g.Occurrences.Select(o => new MagicStringOccurrenceResult
                    {
                        FilePath = o.FilePath,
                        Line = o.Line,
                        TypeName = o.TypeName,
                        MemberName = o.MemberName
                    }).ToList(),
                    SuggestedFixes = FixCatalog.ForMagicString(
                        firstOccurrence?.FilePath,
                        firstOccurrence?.Line ?? 0,
                        solutionPath)
                };
            }).ToList()
        };
    }
}
