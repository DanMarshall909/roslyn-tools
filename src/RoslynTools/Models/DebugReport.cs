namespace RoslynTools.Models;

internal sealed record DebugReport(
    string TracePath,
    int EventCount,
    int FromEvent,
    IReadOnlyList<DebugEvent> Events,
    IReadOnlyList<DebugBreakpointHit> BreakpointHits,
    IReadOnlyList<DebugAssertionResult> Assertions,
    string ReplayCommand);

internal sealed record DebugEvent(
    int Step,
    string? TraceId,
    string EventType,
    string? Tool,
    string Summary,
    IReadOnlyList<string> Files,
    bool BreakpointHit);

internal sealed record DebugBreakpointHit(
    int EventStep,
    string Expression,
    string Reason);

internal sealed record DebugAssertionResult(
    string Expression,
    bool Passed,
    string Message);
