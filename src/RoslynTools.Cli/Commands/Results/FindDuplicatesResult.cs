using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands.Results;

/// <summary>
/// Tracks the occurrence of a symbol in the project, including file and line information.
/// </summary>
internal sealed record OccurrenceResult : IRtOutput
{
    // Not annotated — used only by RenderText for byte-for-byte compat with old PrintGroups
    public required string AbsoluteForwardSlashPath { get; init; }

    // "{absoluteForwardSlash}:{startLine}" — backward-compat JSON link field, also shown in LLM output
    [OutputField(OutputLevel.Llm, "link")] public required string Link { get; init; }

    [OutputField(OutputLevel.Default, "file")]
    public required string File { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    [OutputField(OutputLevel.Default, "endLine")]
    public required int EndLine { get; init; }

    [OutputField(OutputLevel.Default, "type")]
    public required string Type { get; init; }

    [OutputField(OutputLevel.Default, "member")]
    public required string Member { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{AbsoluteForwardSlashPath}:{Line}-{EndLine}");
    }
}

/// <summary>
/// Represents the outcome of a code check, including any issues found or warnings.
/// </summary>
internal sealed record CheckResult : IRtOutput
{
    [OutputField(OutputLevel.Default, "name")]
    public required string Name { get; init; }

    [OutputField(OutputLevel.Default, "status")]
    public required string Status { get; init; }

    [OutputField(OutputLevel.Detailed, "detail")]
    public string? Detail { get; init; }

    public void RenderText(TextWriter writer)
    {
        var detail = Detail is not null ? $": {Detail}" : "";
        writer.Write($"[{Status}] {Name}{detail}");
    }
}

/// <summary>
/// Identifies duplicate groups in the project, helping to detect code duplication.
/// </summary>
internal sealed record DuplicateGroupResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "groupId")]
    public required string GroupId { get; init; }

    [OutputField(OutputLevel.Default, "mode")]
    public static string Mode => "structural";

    [OutputField(OutputLevel.Default, "score")]
    public required double Score { get; init; }

    [OutputField(OutputLevel.Default, "occurrenceCount")]
    public required int OccurrenceCount { get; init; }

    [OutputField(OutputLevel.Llm, "extractability")]
    public required string Extractability { get; init; }

    [OutputField(OutputLevel.Llm, "preview")]
    public required string Preview { get; init; }

    public required string FullBlock { get; init; }

    public required bool PartiallyExpandable { get; init; }

    [OutputField(OutputLevel.Llm, "occurrences")]
    public required IReadOnlyList<OccurrenceResult> Occurrences { get; init; }

    [OutputField(OutputLevel.Default, "checks")]
    public required IReadOnlyList<CheckResult> Checks { get; init; }

    public void RenderText(TextWriter writer)
    {
        throw new NotSupportedException("Use FindDuplicatesResult.RenderText");
    }
}

/// <summary>
/// Detects duplicate symbols in the project and provides information about their occurrences.
/// </summary>
internal sealed record FindDuplicatesResult : IRtOutput, IRtMarkdownOutput, IRtJsonOutput
{
    [OutputField(OutputLevel.Default, "mode")]
    public static string Mode => "structural";

    [OutputField(OutputLevel.Llm, "groups")]
    public required IReadOnlyList<DuplicateGroupResult> Groups { get; init; }

    public void RenderJson(TextWriter writer)
    {
        var doc = new
        {
            mode = Mode,
            groups = Groups.Select(g => new
            {
                groupId = g.GroupId,
                mode = DuplicateGroupResult.Mode,
                score = g.Score,
                occurrenceCount = g.OccurrenceCount,
                extractability = g.Extractability,
                preview = g.Preview,
                occurrences = g.Occurrences.Select(o => new
                {
                    file = o.File,
                    line = o.Line,
                    endLine = o.EndLine,
                    type = o.Type,
                    member = o.Member,
                    link = o.Link
                }).ToArray(),
                checks = g.Checks.Select(c => new
                {
                    name = c.Name,
                    status = c.Status,
                    detail = c.Detail
                }).ToArray()
            }).ToArray()
        };

        writer.Write(JsonSerializer.Serialize(doc, SymbolFormatter.DefaultJsonOptions));
    }

    public void RenderMarkdown(TextWriter writer)
    {
        writer.WriteLine("# Duplicate Groups");
        writer.WriteLine();

        if (Groups.Count == 0)
        {
            writer.WriteLine("No duplicate groups found.");
            return;
        }

        for (var i = 0; i < Groups.Count; i++)
        {
            var g = Groups[i];
            writer.WriteLine($"## Group {i + 1}: {g.GroupId}");
            writer.WriteLine();
            writer.WriteLine($"**Extractability:** {g.Extractability}  ");
            writer.WriteLine($"**Score:** {g.Score:F2}  ");
            writer.WriteLine($"**Occurrences:** {g.OccurrenceCount}  ");
            writer.WriteLine();
            writer.WriteLine("**Preview:**");
            writer.WriteLine();
            writer.WriteLine("```csharp");
            writer.WriteLine(g.FullBlock);
            writer.WriteLine("```");
            writer.WriteLine();

            if (g.Occurrences.Count > 0)
            {
                writer.WriteLine("| # | Location | Type | Member |");
                writer.WriteLine("| --- | --- | --- | --- |");
                for (var j = 0; j < g.Occurrences.Count; j++)
                {
                    var o = g.Occurrences[j];
                    var label = $"{Path.GetFileName(o.File)}:{o.Line}-{o.EndLine}";
                    var href = $"vscode://file/{o.AbsoluteForwardSlashPath}:{o.Line}";
                    var fallback = $"file:///{o.AbsoluteForwardSlashPath}#L{o.Line}";
                    writer.WriteLine(
                        $"| {j + 1} | <a href=\"{href}\" title=\"{fallback}\">{label}</a> | {o.Type} | {o.Member} |");
                }

                writer.WriteLine();
            }

            if (g.Checks is { Count: > 0 })
            {
                writer.WriteLine("| Check | Status | Detail |");
                writer.WriteLine("| --- | --- | --- |");
                foreach (var c in g.Checks) writer.WriteLine($"| {c.Name} | {c.Status} | {c.Detail ?? string.Empty} |");
                writer.WriteLine();
            }
        }
    }

    public void RenderText(TextWriter writer)
    {
        if (Groups.Count == 0)
        {
            writer.WriteLine("No duplicate groups found.");
            return;
        }

        for (var i = 0; i < Groups.Count; i++)
        {
            var g = Groups[i];
            var partialTag = g.PartiallyExpandable ? "  [partial]" : "";
            writer.WriteLine(
                $"Group {i + 1}  id={g.GroupId}  occurrences={g.OccurrenceCount}  score={g.Score:F2}  extractable={g.Extractability}{partialTag}");
            writer.WriteLine($"preview: {g.Preview}");
            if (g.Checks is { Count: > 0 })
                foreach (var c in g.Checks)
                {
                    var detail = c.Detail is not null ? $": {c.Detail}" : "";
                    writer.WriteLine($"  [{c.Status}] {c.Name}{detail}");
                }

            for (var j = 0; j < g.Occurrences.Count; j++)
            {
                var o = g.Occurrences[j];
                writer.WriteLine(
                    $"  {j + 1}. {o.AbsoluteForwardSlashPath}:{o.Line}-{o.EndLine} in {o.Type}.{o.Member}");
            }

            writer.WriteLine();
        }
    }

    internal static FindDuplicatesResult FromGroups(IReadOnlyList<DuplicateGroup> groups, string? solutionDir)
    {
        return new FindDuplicatesResult
        {
            Groups = groups.Select(g => new DuplicateGroupResult
            {
                GroupId = g.GroupId,
                Extractability = g.Extractability,
                Preview = g.Preview,
                FullBlock = g.FullBlock,
                Score = g.Score,
                OccurrenceCount = g.Occurrences.Count,
                PartiallyExpandable = g.PartiallyExpandable,
                Occurrences = g.Occurrences.Select(o =>
                {
                    var absoluteForwardSlash = o.FilePath.Replace('\\', '/');
                    var relativePath = MakeRelativePath(o.FilePath, solutionDir);
                    return new OccurrenceResult
                    {
                        AbsoluteForwardSlashPath = absoluteForwardSlash,
                        Link = $"{absoluteForwardSlash}:{o.StartLine}",
                        File = relativePath,
                        Line = o.StartLine,
                        EndLine = o.EndLine,
                        Type = o.ContainingType,
                        Member = o.ContainingMember
                    };
                }).ToList(),
                Checks = (g.Checks ?? []).Select(c => new CheckResult
                {
                    Name = c.Name,
                    Status = c.Status,
                    Detail = c.Detail
                }).ToList()
            }).ToList()
        };
    }

    private static string MakeRelativePath(string absolutePath, string? solutionDir)
    {
        if (solutionDir is not null)
            try
            {
                return Path.GetRelativePath(solutionDir, absolutePath);
            }
            catch
            {
                // Fall through to filename fallback
            }

        return Path.GetFileName(absolutePath);
    }
}
