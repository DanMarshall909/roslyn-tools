using RoslynTools.Services;

namespace RoslynTools.Tests.Analysis;

public sealed class Debug
{
    [Fact]
    public void analyzes_jsonl_trace_as_timeline()
    {
        var trace = WriteTrace(
            """
            {"event":"user_message","content":"rename the service"}
            {"event":"model_call","content":"I need to inspect the code"}
            {"event":"tool_call","tool":"apply_patch","path":"src/Foo.cs","summary":"edit file"}
            {"event":"final_response","content":"done"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);

        Assert.Equal(4, report.EventCount);
        Assert.Equal(["user_message", "model_call", "tool_call", "final_response"], report.Events.Select(e => e.EventType));
        Assert.Equal("apply_patch", report.Events[2].Tool);
        Assert.Contains("src/Foo.cs", report.Events[2].Files);
    }

    [Fact]
    public void marks_breakpoint_hits()
    {
        var trace = WriteTrace(
            """
            {"event":"model_call","content":"thinking"}
            {"event":"tool_call","tool":"apply_patch","summary":"edit file"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, ["tool:apply_patch"], []);

        var hit = Assert.Single(report.BreakpointHits);
        Assert.Equal(2, hit.EventIndex);
        Assert.True(report.Events[1].BreakpointHit);
    }

    [Fact]
    public void evaluates_assertions_against_full_trace()
    {
        var trace = WriteTrace(
            """
            {"event":"model_call","content":"thinking"}
            {"event":"tool_call","tool":"web","summary":"search"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(
            trace,
            1,
            1,
            [],
            ["no-tool:web", "requires-event:model_call", "max-events:1"]);

        Assert.Equal([false, true, false], report.Assertions.Select(a => a.Passed));
    }

    [Fact]
    public void parses_json_array_traces()
    {
        var trace = WriteTrace(
            """
            [
              {"event":"model_call","content":"thinking"},
              {"event":"final_response","content":"done"}
            ]
            """);

        var report = DebugTraceAnalyzerService.Analyze(trace, 2, 100, [], []);

        var evt = Assert.Single(report.Events);
        Assert.Equal("final_response", evt.EventType);
    }

    [Fact]
    public void parses_single_json_object_trace()
    {
        var trace = WriteTrace(
            """
            {"event":"final_response","content":"done"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);

        var evt = Assert.Single(report.Events);
        Assert.Equal("final_response", evt.EventType);
    }

    [Fact]
    public void ignores_scalar_json_trace_roots()
    {
        var trace = WriteTrace("\"not an event\"");

        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["max-events:0"]);

        Assert.Empty(report.Events);
        Assert.True(Assert.Single(report.Assertions).Passed);
    }

    [Fact]
    public void ignores_empty_assertion_values()
    {
        var trace = WriteTrace(
            """
            {"event":"tool_call","tool":"web","summary":"search"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["no-tool:"]);

        var assertion = Assert.Single(report.Assertions);
        Assert.False(assertion.Passed);
        Assert.Contains("unsupported assertion", assertion.Message);
    }

    [Fact]
    public void debug_command_is_registered_for_mcp_bridge()
    {
        Assert.Contains(App.Commands, command => command.Info.Name == "debug");
    }

    [Fact]
    public void break_only_keeps_only_events_that_hit_breakpoints()
    {
        var trace = WriteTrace(
            """
            {"event":"model_call","content":"thinking"}
            {"event":"tool_call","tool":"apply_patch","summary":"edit file"}
            {"event":"final_response","content":"done"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, ["tool:apply_patch"], [], breakOnly: true);

        var evt = Assert.Single(report.Events);
        Assert.Equal("tool_call", evt.EventType);
        Assert.True(evt.BreakpointHit);
    }

    [Fact]
    public async Task command_returns_exit_2_when_assertion_fails()
    {
        var trace = WriteTrace(
            """
            {"event":"tool_call","tool":"web","summary":"search"}
            """);

        var exitCode = await App.RunAsync([
            "analyze",
            "debug",
            trace.FullName,
            "--assert",
            "no-tool:web"
        ]);

        Assert.Equal(2, exitCode);
    }

    private static FileInfo WriteTrace(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, contents);
        return new FileInfo(path);
    }
}
