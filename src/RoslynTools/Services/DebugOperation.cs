using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Models;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for recorded LLM trace analysis.</summary>
internal sealed record DebugRequest(
    [property: JsonRequired] string Trace,
    int From = 1,
    int Limit = 100,
    [property: JsonPropertyName("break")] string[]? Breakpoints = null,
    [property: JsonPropertyName("assert")] string[]? Assertions = null,
    [property: JsonPropertyName("break-only")] bool BreakOnly = false);

/// <summary>
/// Shared trace-analysis operation. It returns the typed report or an actionable routine
/// error; hosts own binding, rendering, and exit-code projection.
/// </summary>
internal sealed class DebugOperation : CommandApplicationPort<DebugRequest, DebugReport>, IRtService
{
    private readonly Func<
        string,
        int,
        int,
        IReadOnlyList<string>,
        IReadOnlyList<string>,
        bool,
        Result<DebugReport, RtError>> _analyze = DebugTraceAnalyzerService.Analyze;

    public override ApplicationCommandDefinition Definition => DebugCommandDefinition.Definition;

    public override Task<Result<DebugReport, RtError>> ExecuteAsync(
        DebugRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_analyze(
            request.Trace,
            request.From,
            request.Limit,
            request.Breakpoints ?? [],
            request.Assertions ?? [],
            request.BreakOnly));
    }
}

internal static class DebugCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "debug",
        false,
        true,
        "analyze",
        "Use this when diagnosing a recorded LLM or agent trace as a debugger timeline. Prefer this over Read when you need event, tool, text, or file breakpoints and assertions. This does not attach to a live .NET process yet.",
        [
            "rt analyze debug trace.jsonl",
            "rt analyze debug trace.jsonl --break tool:apply_patch --break event:model_call",
            "rt analyze debug trace.jsonl --from 5 --assert no-tool:web --assert requires-event:final_response --json"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(DebugRequest),
        typeof(DebugReport),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        []);
}
