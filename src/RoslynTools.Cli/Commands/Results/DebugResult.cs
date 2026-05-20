using RoslynTools.Core.Output;
using RoslynTools.Models;

namespace RoslynTools.Commands.Results;

internal sealed record DebugEventOutput : IRtOutput
{
    [OutputField(OutputLevel.Llm, "step")]
    public required int Step { get; init; }

    [OutputField(OutputLevel.Default, "traceId")]
    public string? TraceId { get; init; }

    [OutputField(OutputLevel.Llm, "event")]
    public required string EventType { get; init; }

    [OutputField(OutputLevel.Llm, "tool")]
    public string? Tool { get; init; }

    [OutputField(OutputLevel.Llm, "summary")]
    public required string Summary { get; init; }

    [OutputField(OutputLevel.Default, "files")]
    public required IReadOnlyList<string> Files { get; init; }

    [OutputField(OutputLevel.Llm, "breakpointHit")]
    public required bool BreakpointHit { get; init; }

    public void RenderText(TextWriter writer)
    {
        var marker = BreakpointHit ? " *" : string.Empty;
        var tool = Tool is null ? string.Empty : $" [{Tool}]";
        writer.Write($"#{Step} {EventType}{tool}{marker} - {Summary}");
    }
}

internal sealed record DebugBreakpointHitOutput : IRtOutput
{
    [OutputField(OutputLevel.Llm, "eventStep")]
    public required int EventStep { get; init; }

    [OutputField(OutputLevel.Llm, "expression")]
    public required string Expression { get; init; }

    [OutputField(OutputLevel.Default, "reason")]
    public required string Reason { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"#{EventStep} {Expression}: {Reason}");
    }
}

internal sealed record DebugAssertionOutput : IRtOutput
{
    [OutputField(OutputLevel.Llm, "expression")]
    public required string Expression { get; init; }

    [OutputField(OutputLevel.Llm, "passed")]
    public required bool Passed { get; init; }

    [OutputField(OutputLevel.Default, "message")]
    public required string Message { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{(Passed ? "PASS" : "FAIL")} {Expression}: {Message}");
    }
}

internal sealed record DebugResult : IRtOutput, IRtMarkdownOutput
{
    [OutputField(OutputLevel.Default, "trace")]
    public required string TracePath { get; init; }

    [OutputField(OutputLevel.Llm, "eventCount")]
    public required int EventCount { get; init; }

    [OutputField(OutputLevel.Default, "fromEvent")]
    public required int FromEvent { get; init; }

    [OutputField(OutputLevel.Llm, "events")]
    public required IReadOnlyList<DebugEventOutput> Events { get; init; }

    [OutputField(OutputLevel.Llm, "breakpointHits")]
    public required IReadOnlyList<DebugBreakpointHitOutput> BreakpointHits { get; init; }

    [OutputField(OutputLevel.Llm, "assertions")]
    public required IReadOnlyList<DebugAssertionOutput> Assertions { get; init; }

    [OutputField(OutputLevel.Default, "replayCommand")]
    public required string ReplayCommand { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.WriteLine($"Trace: {TracePath}");
        writer.WriteLine($"Events: {EventCount}; showing {Events.Count} from #{FromEvent}");
        writer.WriteLine($"Breakpoints hit: {BreakpointHits.Count}");

        if (Assertions.Count > 0)
            writer.WriteLine($"Assertions: {Assertions.Count(a => a.Passed)}/{Assertions.Count} passed");

        writer.WriteLine();
        writer.WriteLine("Timeline:");
        foreach (var evt in Events)
        {
            writer.Write("  ");
            evt.RenderText(writer);
            writer.WriteLine();
        }

        if (BreakpointHits.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Breakpoint hits:");
            foreach (var hit in BreakpointHits)
            {
                writer.Write("  ");
                hit.RenderText(writer);
                writer.WriteLine();
            }
        }

        if (Assertions.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Assertions:");
            foreach (var assertion in Assertions)
            {
                writer.Write("  ");
                assertion.RenderText(writer);
                writer.WriteLine();
            }
        }

        writer.WriteLine();
        writer.WriteLine($"Replay: {ReplayCommand}");
    }

    public void RenderMarkdown(TextWriter writer)
    {
        writer.WriteLine("# LLM Debug Trace");
        writer.WriteLine();
        writer.WriteLine($"Trace: `{TracePath}`  ");
        writer.WriteLine($"Events: **{EventCount}**; showing **{Events.Count}** from `#{FromEvent}`  ");
        writer.WriteLine($"Breakpoints hit: **{BreakpointHits.Count}**  ");
        writer.WriteLine();
        writer.WriteLine("## Timeline");
        writer.WriteLine();
        foreach (var evt in Events)
            writer.WriteLine($"- `#{evt.Step}` **{evt.EventType}**{(evt.Tool is null ? "" : $" `{evt.Tool}`")}{(evt.BreakpointHit ? " **break**" : "")}: {evt.Summary}");

        if (BreakpointHits.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Breakpoint Hits");
            writer.WriteLine();
            foreach (var hit in BreakpointHits)
                writer.WriteLine($"- `#{hit.EventStep}` `{hit.Expression}` - {hit.Reason}");
        }

        if (Assertions.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Assertions");
            writer.WriteLine();
            foreach (var assertion in Assertions)
                writer.WriteLine($"- **{(assertion.Passed ? "PASS" : "FAIL")}** `{assertion.Expression}` - {assertion.Message}");
        }

        writer.WriteLine();
        writer.WriteLine("## Replay");
        writer.WriteLine();
        writer.WriteLine("```bash");
        writer.WriteLine(ReplayCommand);
        writer.WriteLine("```");
    }

    internal static DebugResult FromReport(DebugReport report)
    {
        return new DebugResult
        {
            TracePath = report.TracePath,
            EventCount = report.EventCount,
            FromEvent = report.FromEvent,
            Events = report.Events.Select(evt => new DebugEventOutput
            {
                Step = evt.Step,
                TraceId = evt.TraceId,
                EventType = evt.EventType,
                Tool = evt.Tool,
                Summary = evt.Summary,
                Files = evt.Files,
                BreakpointHit = evt.BreakpointHit
            }).ToList(),
            BreakpointHits = report.BreakpointHits.Select(hit => new DebugBreakpointHitOutput
            {
                EventStep = hit.EventStep,
                Expression = hit.Expression,
                Reason = hit.Reason
            }).ToList(),
            Assertions = report.Assertions.Select(assertion => new DebugAssertionOutput
            {
                Expression = assertion.Expression,
                Passed = assertion.Passed,
                Message = assertion.Message
            }).ToList(),
            ReplayCommand = report.ReplayCommand
        };
    }
}
