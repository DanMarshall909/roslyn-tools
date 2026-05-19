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
    int Index,
    string EventType,
    string? Tool,
    string Summary,
    IReadOnlyList<string> Files,
    bool BreakpointHit);

internal sealed record DebugBreakpointHit(
    int EventIndex,
    string Expression,
    string Reason);

internal sealed record DebugAssertionResult(
    string Expression,
    bool Passed,
    string Message);
