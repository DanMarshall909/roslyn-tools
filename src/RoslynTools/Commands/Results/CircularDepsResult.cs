using RoslynTools.Core.Output;

namespace RoslynTools.Commands.Results;

internal sealed record CycleResult : IRtOutput
{
    [OutputField(OutputLevel.Llm, "path")]
    public required string Path { get; init; }

    [OutputField(OutputLevel.Default, "nodes")]
    public required IReadOnlyList<string> Nodes { get; init; }

    public void RenderText(TextWriter writer) => writer.Write(Path);
}

internal sealed record CircularDepsResult : IRtOutput, IRtMarkdownOutput
{
    [OutputField(OutputLevel.Llm, "hasCycles")]
    public required bool HasCycles { get; init; }

    [OutputField(OutputLevel.Llm, "cycles")]
    public required IReadOnlyList<CycleResult> Cycles { get; init; }

    public void RenderText(TextWriter writer)
    {
        if (!HasCycles)
        {
            writer.WriteLine("No circular namespace dependencies found.");
            return;
        }

        for (var i = 0; i < Cycles.Count; i++)
        {
            writer.WriteLine(Cycles[i].Path);
        }
    }

    public void RenderMarkdown(TextWriter writer)
    {
        writer.WriteLine("# Circular Namespace Dependencies");
        writer.WriteLine();

        if (!HasCycles)
        {
            writer.WriteLine("No circular namespace dependencies found.");
            return;
        }

        for (var i = 0; i < Cycles.Count; i++)
        {
            var cycle = Cycles[i];
            writer.WriteLine($"## Cycle {i + 1}");
            writer.WriteLine();
            writer.WriteLine($"**Path:** `{cycle.Path}`");
            writer.WriteLine();
            writer.WriteLine("**Nodes:**");
            writer.WriteLine();
            foreach (var node in cycle.Nodes)
            {
                writer.WriteLine($"- {node}");
            }
            writer.WriteLine();
        }
    }

    internal static CircularDepsResult FromCycles(IReadOnlyList<IReadOnlyList<string>> cycles)
    {
        var cycleResults = cycles.Select(nodes =>
        {
            var path = string.Join(" -> ", nodes);
            return new CycleResult
            {
                Path = path,
                Nodes = nodes,
            };
        }).ToList();

        return new CircularDepsResult
        {
            HasCycles = cycleResults.Count > 0,
            Cycles = cycleResults,
        };
    }
}
