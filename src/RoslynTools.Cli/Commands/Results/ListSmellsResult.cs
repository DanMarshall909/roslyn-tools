using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands.Results;

/// <summary>
/// Indicates the presence of a code smell item, including its type and location.
/// </summary>
internal sealed record SmellItemResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "smellType")]
    public required string SmellType { get; init; }

    [OutputField(OutputLevel.Llm, "typeName")]
    public required string TypeName { get; init; }

    [OutputField(OutputLevel.Default, "memberName")]
    public required string? MemberName { get; init; }

    [OutputField(OutputLevel.Default, "filePath")]
    public required string FilePath { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    [OutputField(OutputLevel.Default, "detail")]
    public required string Detail { get; init; }

    public IReadOnlyList<SuggestedFix> SuggestedFixes { get; init; } = [];

    public void RenderText(TextWriter writer)
    {
        var member = MemberName is not null ? $"  {MemberName}" : "";
        writer.Write($"{SmellType,-24}{TypeName,-30}{member,-20}{FilePath}:{Line}  {Detail}");
        foreach (var fix in SuggestedFixes)
        {
            writer.WriteLine();
            fix.RenderText(writer);
        }
    }
}

/// <summary>
/// Lists all code smell items found in the project.
/// </summary>
internal sealed record ListSmellsResult : IRtOutput, IRtMarkdownOutput, IRtJsonOutput
{
    [OutputField(OutputLevel.Llm, "smells")]
    public required IReadOnlyList<SmellItemResult> Smells { get; init; }

    public void RenderJson(TextWriter writer)
    {
        var doc = new
        {
            smells = Smells.Select(s => new
            {
                smellType = s.SmellType,
                typeName = s.TypeName,
                memberName = s.MemberName,
                filePath = s.FilePath,
                line = s.Line,
                detail = s.Detail,
                suggestedFixes = s.SuggestedFixes.Select(f => f.AsJsonObject()).ToArray()
            }).ToArray()
        };
        writer.Write(JsonSerializer.Serialize(doc, SymbolFormatter.DefaultJsonOptions));
    }

    public void RenderMarkdown(TextWriter writer)
    {
        writer.WriteLine("# Code Smells");
        writer.WriteLine();

        if (Smells.Count == 0)
        {
            writer.WriteLine("No code smells found.");
            return;
        }

        var groups = Smells.GroupBy(s => s.SmellType);
        foreach (var group in groups)
        {
            writer.WriteLine($"## {group.Key}");
            writer.WriteLine();
            writer.WriteLine("| Type | Member | File | Line | Detail |");
            writer.WriteLine("| --- | --- | --- | --- | --- |");
            foreach (var smell in group)
            {
                var member = smell.MemberName ?? string.Empty;
                writer.WriteLine($"| {smell.TypeName} | {member} | {smell.FilePath} | {smell.Line} | {smell.Detail} |");
            }

            var fixes = group.First().SuggestedFixes;
            if (fixes.Count > 0)
            {
                writer.WriteLine();
                writer.WriteLine("**Suggested fixes:**");
                writer.WriteLine();
                foreach (var fix in fixes)
                {
                    if (fix.CommandLine is not null)
                        writer.WriteLine($"- `{fix.Tool}`: `{fix.CommandLine}` *(replace `<placeholders>`)*");
                    else
                        writer.WriteLine($"- `{fix.Tool}`: {fix.Ref} — {fix.Description}");
                }
            }

            writer.WriteLine();
        }
    }

    public void RenderText(TextWriter writer)
    {
        foreach (var smell in Smells)
        {
            smell.RenderText(writer);
            writer.WriteLine();
        }
    }
}
