using RoslynTools.Services;
using Shouldly;

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

        report.EventCount.ShouldBe(4);
        report.Events.Select(e => e.EventType).ShouldBe(["user_message", "model_call", "tool_call", "final_response"]);
        report.Events[2].Tool.ShouldBe("apply_patch");
        report.Events[2].Files.ShouldContain("src/Foo.cs");
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
        hit.EventStep.ShouldBe(2);
        report.Events[1].BreakpointHit.ShouldBeTrue();
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

        report.Assertions.Select(a => a.Passed).ShouldBe([false, true, false]);
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
        evt.EventType.ShouldBe("final_response");
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
        evt.EventType.ShouldBe("final_response");
    }

    [Fact]
    public void from_filters_by_step_not_trace_id()
    {
        var trace = WriteTrace(
            """
            {"id":100,"event":"model_call","content":"thinking"}
            {"id":200,"event":"tool_call","tool":"apply_patch","summary":"edit file"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(trace, 2, 1, [], []);

        var evt = Assert.Single(report.Events);
        evt.Step.ShouldBe(2);
        evt.TraceId.ShouldBe("200");
        evt.EventType.ShouldBe("tool_call");
    }

    [Fact]
    public void ignores_scalar_json_trace_roots()
    {
        var trace = WriteTrace("\"not an event\"");

        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["max-events:0"]);

        report.Events.ShouldBeEmpty();
        Assert.Single(report.Assertions).Passed.ShouldBeTrue();
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
        assertion.Passed.ShouldBeFalse();
        assertion.Message.ShouldContain("unsupported assertion", Case.Sensitive);
    }

    [Fact]
    public void debug_command_is_registered_for_mcp_bridge()
    {
        App.Commands.ShouldContain(command => command.Info.Name == "debug");
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
        evt.EventType.ShouldBe("tool_call");
        evt.BreakpointHit.ShouldBeTrue();
    }

    [Fact]
    public void file_breakpoints_match_file_arrays()
    {
        var trace = WriteTrace(
            """
            {"event":"tool_call","tool":"apply_patch","files":["src/Foo.cs"],"summary":"edit file"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(
            trace,
            1,
            100,
            ["file:src/Foo.cs"],
            [],
            breakOnly: true);

        var evt = Assert.Single(report.Events);
        evt.Files.ShouldBe(["src/Foo.cs"]);
        evt.BreakpointHit.ShouldBeTrue();
    }

    [Fact]
    public void file_breakpoints_match_nested_path_arrays()
    {
        var trace = WriteTrace(
            """
            {"event":"tool_result","result":{"paths":["src/Foo.cs","tests/FooTests.cs"]},"summary":"edited files"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(
            trace,
            1,
            100,
            ["file:tests/FooTests.cs"],
            [],
            breakOnly: true);

        var evt = Assert.Single(report.Events);
        evt.Files.ShouldBe(["src/Foo.cs", "tests/FooTests.cs"]);
        evt.BreakpointHit.ShouldBeTrue();
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

        exitCode.ShouldBe(2);
    }

    // ─── event-level field extraction (tool, event type, trace id) ───────────
    //
    // Each row exercises one shape of the BuildEvent fallback chains. Together
    // they kill all null-coalescing precedence + leaf string mutations on
    // L103-115 plus GetNestedString's value-kind guard at L230.

    [Theory]
    [InlineData("""{"event":"tool_call","tool":"first"}""", "first")]
    [InlineData("""{"event":"tool_call","toolName":"second"}""", "second")]
    [InlineData("""{"event":"tool_call","tool_call":{"name":"third"}}""", "third")]
    [InlineData("""{"event":"tool_call","toolCall":{"name":"fourth"}}""", "fourth")]
    [InlineData("""{"event":"tool_call","tool":"first","toolName":"second"}""", "first")]
    [InlineData("""{"event":"tool_call","toolName":"second","tool_call":{"name":"third"}}""", "second")]
    [InlineData("""{"event":"tool_call","tool_call":{"name":"third"},"toolCall":{"name":"fourth"}}""", "third")]
    [InlineData("""{"event":"model_call"}""", null)]
    [InlineData("""{"event":"tool_call","tool_call":"not-an-object"}""", null)]
    public void tool_name_resolves_through_priority_chain(string traceJson, string? expectedTool)
    {
        var trace = WriteTrace(traceJson);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Single(report.Events).Tool.ShouldBe(expectedTool);
    }

    [Theory]
    [InlineData("""{"event":"chat","content":"x"}""", "chat")]
    [InlineData("""{"type":"system","content":"x"}""", "system")]
    [InlineData("""{"name":"phase","content":"x"}""", "phase")]
    [InlineData("""{"content":"x"}""", "unknown")]
    public void event_type_resolves_through_priority_chain(string traceJson, string expectedType)
    {
        var trace = WriteTrace(traceJson);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Single(report.Events).EventType.ShouldBe(expectedType);
    }

    [Theory]
    [InlineData("""{"id":"abc-123","event":"chat"}""", "abc-123")]
    [InlineData("""{"id":42,"event":"chat"}""", "42")]
    [InlineData("""{"id":true,"event":"chat"}""", null)]
    [InlineData("""{"event":"chat"}""", null)]
    public void trace_id_is_captured_only_for_string_or_number_id_field(string traceJson, string? expectedTraceId)
    {
        var trace = WriteTrace(traceJson);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Single(report.Events).TraceId.ShouldBe(expectedTraceId);
    }

    // ─── summary fallback chain + format ─────────────────────────────────────

    [Theory]
    [InlineData("""{"event":"chat","summary":"S","message":"M","content":"C","error":"E"}""", "chat: S")]
    [InlineData("""{"event":"chat","message":"M","content":"C","error":"E"}""", "chat: M")]
    [InlineData("""{"event":"chat","content":"C","error":"E"}""", "chat: C")]
    [InlineData("""{"event":"failure","error":"E"}""", "failure: E")]
    public void summary_picks_first_present_field_in_priority_order(string traceJson, string expectedSummary)
    {
        var trace = WriteTrace(traceJson);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Single(report.Events).Summary.ShouldBe(expectedSummary);
    }

    [Fact]
    public void summary_falls_back_to_raw_json_when_no_text_field_present()
    {
        var trace = WriteTrace("""{"event":"weird","extra":"data"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Single(report.Events).Summary.ShouldContain("\"extra\":\"data\"", Case.Sensitive);
    }

    [Theory]
    [InlineData("""{"event":"tool_call","tool":"apply_patch","summary":"edit"}""", "tool_call apply_patch: edit")]
    [InlineData("""{"event":"model_call","summary":"thinking"}""", "model_call: thinking")]
    public void summary_prefix_includes_tool_only_when_present(string traceJson, string expectedSummary)
    {
        var trace = WriteTrace(traceJson);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Single(report.Events).Summary.ShouldBe(expectedSummary);
    }

    [Fact]
    public void summary_collapses_embedded_newlines_to_spaces()
    {
        var trace = WriteTrace("""{"event":"chat","summary":"line one\nline two"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        var summary = Assert.Single(report.Events).Summary;
        summary.ShouldContain("line one line two", Case.Sensitive);
        summary.ShouldNotContain('\n');
    }

    [Theory]
    // Three Truncate cases anchored to "chat: " (6-char prefix) + SummaryLimit (160). Together
    // they kill the value.Length <= limit boundary mutation and the limit - 3 arithmetic mutation.
    [InlineData(2, 8, false)]   // far under limit → "chat: xx" (8 chars), no ellipsis
    [InlineData(154, 160, false)]   // boundary: full summary exactly 160 chars, no ellipsis
    [InlineData(200, 160, true)]    // over limit → truncated to 160 chars ending in "..."
    public void summary_truncation_respects_160_char_limit(int rawSummaryLength, int expectedSummaryLength, bool expectEllipsis)
    {
        var rawSummary = new string('x', rawSummaryLength);
        var trace = WriteTrace($$"""{"event":"chat","summary":"{{rawSummary}}"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        var summary = Assert.Single(report.Events).Summary;

        summary.Length.ShouldBe(expectedSummaryLength);
        summary.EndsWith("...", StringComparison.Ordinal).ShouldBe(expectEllipsis);
    }

    // ─── breakpoint matching ──────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"event":"model_call","content":"think"}""", "event:model_call", "event type is 'model_call'")]
    [InlineData("""{"event":"tool_call","tool":"apply_patch","summary":"edit"}""", "tool:apply_patch", "tool is 'apply_patch'")]
    [InlineData("""{"event":"chat","summary":"contains TODO marker"}""", "text:todo", "summary contains 'todo'")]
    [InlineData("""{"event":"tool_call","tool":"apply_patch","files":["src/Foo.cs"],"summary":"edit"}""", "file:Foo", "file reference matches 'Foo'")]
    public void breakpoint_records_quoted_reason_for_each_expression_kind(string traceJson, string breakpoint, string expectedReason)
    {
        var trace = WriteTrace(traceJson);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [breakpoint], []);
        var hit = Assert.Single(report.BreakpointHits);
        hit.Expression.ShouldBe(breakpoint);
        hit.Reason.ShouldBe(expectedReason);
        report.Events[0].BreakpointHit.ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"event":"tool_call","tool":"x"}""", "event:model_call")]  // non-matching event type
    [InlineData("""{"event":"chat","summary":"hi"}""", "   ")]                  // whitespace-only expression
    public void breakpoint_does_not_hit_when_expression_does_not_match(string traceJson, string breakpoint)
    {
        var trace = WriteTrace(traceJson);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [breakpoint], []);
        report.BreakpointHits.ShouldBeEmpty();
        report.Events[0].BreakpointHit.ShouldBeFalse();
    }

    // ─── assertion messages ───────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"event":"chat","summary":"hi"}""", "no-tool:web", true, "tool 'web' was not used")]
    [InlineData("""{"event":"final_response","content":"done"}""", "requires-event:final_response", true, "event 'final_response' was present")]
    [InlineData("""{"event":"model_call","content":"think"}""", "requires-event:final_response", false, "event 'final_response' was missing")]
    [InlineData("""{"event":"chat","summary":"hi"}""", "max-events:2", true, "trace contains 1 event(s); maximum is 2")]
    [InlineData("""{"event":"chat","summary":"all clean"}""", "no-text:TODO", true, "text 'TODO' was not found")]
    [InlineData("""{"event":"chat","summary":"has TODO inside"}""", "no-text:TODO", false, "text 'TODO' was found")]
    [InlineData("""{"event":"chat","summary":"hi"}""", "   ", true, "empty assertion ignored")]
    public void assertion_result_matches_expression_kind_and_outcome(
        string traceJson,
        string assertion,
        bool expectedPassed,
        string expectedMessage)
    {
        var trace = WriteTrace(traceJson);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], [assertion]);
        var result = Assert.Single(report.Assertions);
        result.Passed.ShouldBe(expectedPassed);
        result.Message.ShouldBe(expectedMessage);
    }

    [Fact]
    public void no_tool_assertion_fail_message_includes_actual_count()
    {
        // Two-event case verifies the "{count} time(s)" branch of no-tool's failure message.
        var trace = WriteTrace(
            """
            {"event":"tool_call","tool":"web","summary":"first"}
            {"event":"tool_call","tool":"web","summary":"second"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["no-tool:web"]);
        var result = Assert.Single(report.Assertions);
        result.Passed.ShouldBeFalse();
        result.Message.ShouldBe("tool 'web' was used 2 time(s)");
    }

    [Fact]
    public void max_events_assertion_message_includes_actual_and_limit_when_exceeded()
    {
        var trace = WriteTrace(
            """
            {"event":"model_call"}
            {"event":"tool_call","tool":"x"}
            {"event":"final_response"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["max-events:2"]);
        var result = Assert.Single(report.Assertions);
        result.Passed.ShouldBeFalse();
        result.Message.ShouldBe("trace contains 3 event(s); maximum is 2");
    }

    [Fact]
    public void no_text_assertion_fails_when_any_event_in_multi_event_trace_contains_text()
    {
        // Kills L191 Linq Any→All mutation: with All, this assertion would pass when only one of
        // two events contained the banned text. Single-event rows in the Theory above can't kill
        // it because Any and All agree when there's only one element.
        var trace = WriteTrace(
            """
            {"event":"chat","summary":"clean"}
            {"event":"chat","summary":"has TODO marker"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["no-text:TODO"]);
        var result = Assert.Single(report.Assertions);
        result.Passed.ShouldBeFalse();
    }

    // ─── file reference extraction ────────────────────────────────────────────

    [Theory]
    [InlineData("""{"event":"chat","summary":"plain text"}""", new string[0])]
    [InlineData("""{"event":"chat","tags":["alpha","beta"],"summary":"x"}""", new string[0])]
    [InlineData("""{"event":"tool_call","output":{"path":"src/A.cs","other":"B"},"summary":"x"}""", new[] { "src/A.cs" })]
    public void file_references_are_yielded_only_under_file_like_property_context(string traceJson, string[] expectedFiles)
    {
        var trace = WriteTrace(traceJson);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Single(report.Events).Files.ShouldBe(expectedFiles);
    }

    // ─── trace loading edge cases ─────────────────────────────────────────────

    [Fact]
    public void analyze_throws_file_not_found_with_path_and_message_when_trace_missing()
    {
        var missing = new FileInfo(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jsonl"));

        var ex = Should.Throw<FileNotFoundException>(() =>
            DebugTraceAnalyzerService.Analyze(missing, 1, 100, [], []));

        ex.FileName.ShouldBe(missing.FullName);
        ex.Message.ShouldContain("LLM trace file was not found", Case.Sensitive);
    }

    [Fact]
    public void jsonl_loader_skips_blank_lines()
    {
        var trace = WriteTrace("{\"event\":\"first\"}\n\n   \n{\"event\":\"second\"}\n");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        report.Events.Select(e => e.EventType).ShouldBe(["first", "second"]);
    }

    [Fact]
    public void empty_trace_file_yields_no_events()
    {
        var trace = WriteTrace("   ");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        report.Events.ShouldBeEmpty();
        report.EventCount.ShouldBe(0);
    }

    // ─── report metadata + limit / from boundaries ────────────────────────────

    [Theory]
    // Math.Max(1, fromEvent) clamp: 0 → 1, 3 → 3. Kills L51 + L55 Max→Min mutations on both sides.
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    public void from_event_is_clamped_to_one_and_reflected_in_report_metadata(int fromEvent, int expectedFromInReport)
    {
        var trace = WriteTrace(
            """
            {"event":"a"}
            {"event":"b"}
            {"event":"c"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, fromEvent, 100, [], []);
        report.FromEvent.ShouldBe(expectedFromInReport);
        report.ReplayCommand.ShouldEndWith($"--from {expectedFromInReport}", Case.Sensitive);
    }

    [Theory]
    // limit <= 0 ? MaxValue : limit. Kills L27 conditional + equality mutations.
    [InlineData(0, 5)]
    [InlineData(3, 3)]
    public void limit_zero_means_unlimited_otherwise_caps_kept_events(int limit, int expectedEventCount)
    {
        var trace = WriteTrace(
            """
            {"event":"a"}
            {"event":"b"}
            {"event":"c"}
            {"event":"d"}
            {"event":"e"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, limit, [], []);
        report.Events.Count.ShouldBe(expectedEventCount);
    }

    [Fact]
    public void event_breakpoint_hit_flag_is_false_when_no_breakpoints_match()
    {
        // Locks down BuildEvent's BreakpointHit=false default — Analyze() rewrites it via
        // `evt with { BreakpointHit = ... }` so the initial value is unobservable, but this
        // verifies the end-to-end observable: no breakpoints → flag is false.
        var trace = WriteTrace("""{"event":"chat","summary":"hi"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Single(report.Events).BreakpointHit.ShouldBeFalse();
    }

    private static FileInfo WriteTrace(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, contents);
        return new FileInfo(path);
    }
}
