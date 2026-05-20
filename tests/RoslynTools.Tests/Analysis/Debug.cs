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
        Assert.Equal(2, hit.EventStep);
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
    public void from_filters_by_step_not_trace_id()
    {
        var trace = WriteTrace(
            """
            {"id":100,"event":"model_call","content":"thinking"}
            {"id":200,"event":"tool_call","tool":"apply_patch","summary":"edit file"}
            """);

        var report = DebugTraceAnalyzerService.Analyze(trace, 2, 1, [], []);

        var evt = Assert.Single(report.Events);
        Assert.Equal(2, evt.Step);
        Assert.Equal("200", evt.TraceId);
        Assert.Equal("tool_call", evt.EventType);
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
        Assert.Equal(["src/Foo.cs"], evt.Files);
        Assert.True(evt.BreakpointHit);
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
        Assert.Equal(["src/Foo.cs", "tests/FooTests.cs"], evt.Files);
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

    // ─── tool-name fallback chain (BuildEvent.tool) ───────────────────────────

    [Fact]
    public void tool_name_uses_tool_field_first()
    {
        var trace = WriteTrace("""{"event":"tool_call","tool":"first"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("first", Assert.Single(report.Events).Tool);
    }

    [Fact]
    public void tool_name_falls_back_to_toolName_when_tool_absent()
    {
        var trace = WriteTrace("""{"event":"tool_call","toolName":"second"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("second", Assert.Single(report.Events).Tool);
    }

    [Fact]
    public void tool_name_falls_back_to_nested_tool_call_name()
    {
        var trace = WriteTrace("""{"event":"tool_call","tool_call":{"name":"third"}}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("third", Assert.Single(report.Events).Tool);
    }

    [Fact]
    public void tool_name_falls_back_to_nested_toolCall_name()
    {
        var trace = WriteTrace("""{"event":"tool_call","toolCall":{"name":"fourth"}}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("fourth", Assert.Single(report.Events).Tool);
    }

    [Fact]
    public void tool_name_is_null_when_no_tool_field_present()
    {
        var trace = WriteTrace("""{"event":"model_call"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Null(Assert.Single(report.Events).Tool);
    }

    [Fact]
    public void nested_tool_call_is_ignored_when_property_is_not_an_object()
    {
        var trace = WriteTrace("""{"event":"tool_call","tool_call":"not-an-object"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Null(Assert.Single(report.Events).Tool);
    }

    [Fact]
    public void tool_name_prefers_tool_field_over_toolName()
    {
        // Kills L108 null-coalescing left-to-right swap.
        var trace = WriteTrace("""{"event":"tool_call","tool":"first","toolName":"second"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("first", Assert.Single(report.Events).Tool);
    }

    [Fact]
    public void tool_name_prefers_toolName_over_nested_tool_call_name()
    {
        // Kills L109 null-coalescing left-to-right swap.
        var trace = WriteTrace("""{"event":"tool_call","toolName":"second","tool_call":{"name":"third"}}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("second", Assert.Single(report.Events).Tool);
    }

    [Fact]
    public void tool_name_prefers_tool_call_name_over_toolCall_name()
    {
        // Kills L110 null-coalescing left-to-right swap.
        var trace = WriteTrace("""{"event":"tool_call","tool_call":{"name":"third"},"toolCall":{"name":"fourth"}}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("third", Assert.Single(report.Events).Tool);
    }

    // ─── event-type fallback (BuildEvent.eventType) ───────────────────────────

    [Fact]
    public void event_type_falls_back_to_type_field()
    {
        var trace = WriteTrace("""{"type":"system","content":"hello"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("system", Assert.Single(report.Events).EventType);
    }

    [Fact]
    public void event_type_falls_back_to_name_field()
    {
        var trace = WriteTrace("""{"name":"phase","content":"hello"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("phase", Assert.Single(report.Events).EventType);
    }

    [Fact]
    public void event_type_is_unknown_when_no_known_field_present()
    {
        var trace = WriteTrace("""{"content":"hello"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("unknown", Assert.Single(report.Events).EventType);
    }

    // ─── summary fallback chain (BuildSummary.explicitSummary) ───────────────

    [Fact]
    public void summary_falls_back_to_message_when_summary_absent()
    {
        var trace = WriteTrace("""{"event":"chat","message":"from-message"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("chat: from-message", Assert.Single(report.Events).Summary);
    }

    [Fact]
    public void summary_falls_back_to_content_when_summary_and_message_absent()
    {
        var trace = WriteTrace("""{"event":"chat","content":"from-content"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("chat: from-content", Assert.Single(report.Events).Summary);
    }

    [Fact]
    public void summary_falls_back_to_error_when_other_fields_absent()
    {
        var trace = WriteTrace("""{"event":"failure","error":"from-error"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("failure: from-error", Assert.Single(report.Events).Summary);
    }

    [Fact]
    public void summary_prefers_summary_field_over_message_content_and_error()
    {
        // Kills L125 null-coalescing left-to-right swap + L125 remove-right mutations.
        var trace = WriteTrace("""{"event":"chat","summary":"S","message":"M","content":"C","error":"E"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("chat: S", Assert.Single(report.Events).Summary);
    }

    [Fact]
    public void summary_prefers_message_over_content_and_error_when_summary_absent()
    {
        // Kills L126 null-coalescing left-to-right + remove-left/right + string "" mutations.
        var trace = WriteTrace("""{"event":"chat","message":"M","content":"C","error":"E"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("chat: M", Assert.Single(report.Events).Summary);
    }

    [Fact]
    public void summary_prefers_content_over_error_when_summary_and_message_absent()
    {
        // Kills L127 null-coalescing left-to-right + remove-left/right + string "" mutations.
        var trace = WriteTrace("""{"event":"chat","content":"C","error":"E"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("chat: C", Assert.Single(report.Events).Summary);
    }

    [Fact]
    public void summary_uses_raw_json_when_no_summary_field_present()
    {
        var trace = WriteTrace("""{"event":"weird","extra":"data"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        // No summary/message/content/error → falls through to GetRawText
        Assert.Contains("\"extra\":\"data\"", Assert.Single(report.Events).Summary);
    }

    [Fact]
    public void summary_collapses_embedded_newlines_to_spaces()
    {
        var trace = WriteTrace("""{"event":"chat","summary":"line one\nline two"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        var summary = Assert.Single(report.Events).Summary;
        Assert.Contains("line one line two", summary);
        Assert.DoesNotContain('\n', summary);
    }

    [Fact]
    public void summary_truncates_long_text_with_ellipsis_at_160_chars()
    {
        // Exercises Truncate's > limit branch: value.Length 200 > 160 → take [..157] + "..." → 160 chars.
        var longText = new string('x', 200);
        var trace = WriteTrace($$"""{"event":"chat","summary":"{{longText}}"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        var summary = Assert.Single(report.Events).Summary;
        Assert.Equal(160, summary.Length);
        Assert.EndsWith("...", summary);
    }

    [Fact]
    public void summary_keeps_short_text_intact_without_ellipsis()
    {
        // Exercises Truncate's <= limit branch: "chat: hi" is far under 160 chars → unchanged.
        var trace = WriteTrace("""{"event":"chat","summary":"hi"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("chat: hi", Assert.Single(report.Events).Summary);
    }

    [Fact]
    public void summary_at_exactly_160_chars_is_not_truncated()
    {
        // Kills L307 equality mutation (value.Length <= limit → < limit). At the 160-char boundary,
        // the original returns the value unchanged; the mutation would force a truncate-with-ellipsis.
        // prefix "chat: " is 6 chars, so 154 trailing x's makes the full summary 160 chars.
        var trailing = new string('x', 154);
        var trace = WriteTrace($$"""{"event":"chat","summary":"{{trailing}}"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        var summary = Assert.Single(report.Events).Summary;
        Assert.Equal(160, summary.Length);
        Assert.EndsWith("x", summary);
        Assert.DoesNotContain("...", summary);
    }

    // ─── summary prefix with/without tool ────────────────────────────────────

    [Fact]
    public void summary_prefix_includes_tool_when_tool_present()
    {
        var trace = WriteTrace("""{"event":"tool_call","tool":"apply_patch","summary":"edit"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("tool_call apply_patch: edit", Assert.Single(report.Events).Summary);
    }

    [Fact]
    public void summary_prefix_omits_tool_when_tool_absent()
    {
        var trace = WriteTrace("""{"event":"model_call","summary":"thinking"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("model_call: thinking", Assert.Single(report.Events).Summary);
    }

    // ─── breakpoint matching ──────────────────────────────────────────────────

    [Fact]
    public void event_breakpoint_hits_matching_event_type_and_records_reason()
    {
        var trace = WriteTrace("""{"event":"model_call","content":"think"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, ["event:model_call"], []);
        var hit = Assert.Single(report.BreakpointHits);
        Assert.Equal("event type is 'model_call'", hit.Reason);
        Assert.Equal("event:model_call", hit.Expression);
        Assert.True(report.Events[0].BreakpointHit);
    }

    [Fact]
    public void event_breakpoint_does_not_hit_non_matching_event_type()
    {
        var trace = WriteTrace("""{"event":"tool_call","tool":"x"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, ["event:model_call"], []);
        Assert.Empty(report.BreakpointHits);
        Assert.False(report.Events[0].BreakpointHit);
    }

    [Fact]
    public void tool_breakpoint_reason_quotes_actual_tool_name()
    {
        var trace = WriteTrace("""{"event":"tool_call","tool":"apply_patch","summary":"edit"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, ["tool:apply_patch"], []);
        Assert.Equal("tool is 'apply_patch'", Assert.Single(report.BreakpointHits).Reason);
    }

    [Fact]
    public void text_breakpoint_hits_when_summary_contains_text_and_records_reason()
    {
        var trace = WriteTrace("""{"event":"chat","summary":"contains TODO marker"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, ["text:todo"], []);
        var hit = Assert.Single(report.BreakpointHits);
        Assert.Equal("summary contains 'todo'", hit.Reason);
    }

    [Fact]
    public void file_breakpoint_reason_quotes_the_filter_text()
    {
        var trace = WriteTrace("""{"event":"tool_call","tool":"apply_patch","files":["src/Foo.cs"],"summary":"edit"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, ["file:Foo"], []);
        Assert.Equal("file reference matches 'Foo'", Assert.Single(report.BreakpointHits).Reason);
    }

    [Fact]
    public void empty_breakpoint_expression_is_ignored()
    {
        var trace = WriteTrace("""{"event":"chat","summary":"hi"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, ["   "], []);
        Assert.Empty(report.BreakpointHits);
    }

    // ─── assertion messages ───────────────────────────────────────────────────

    [Fact]
    public void no_tool_assertion_pass_message_quotes_tool_name()
    {
        var trace = WriteTrace("""{"event":"chat","summary":"hi"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["no-tool:web"]);
        var result = Assert.Single(report.Assertions);
        Assert.True(result.Passed);
        Assert.Equal("tool 'web' was not used", result.Message);
    }

    [Fact]
    public void no_tool_assertion_fail_message_includes_count()
    {
        var trace = WriteTrace(
            """
            {"event":"tool_call","tool":"web","summary":"first"}
            {"event":"tool_call","tool":"web","summary":"second"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["no-tool:web"]);
        var result = Assert.Single(report.Assertions);
        Assert.False(result.Passed);
        Assert.Equal("tool 'web' was used 2 time(s)", result.Message);
    }

    [Fact]
    public void requires_event_assertion_pass_message_quotes_event_name()
    {
        var trace = WriteTrace("""{"event":"final_response","content":"done"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["requires-event:final_response"]);
        var result = Assert.Single(report.Assertions);
        Assert.True(result.Passed);
        Assert.Equal("event 'final_response' was present", result.Message);
    }

    [Fact]
    public void requires_event_assertion_fail_message_quotes_missing_event()
    {
        var trace = WriteTrace("""{"event":"model_call","content":"think"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["requires-event:final_response"]);
        var result = Assert.Single(report.Assertions);
        Assert.False(result.Passed);
        Assert.Equal("event 'final_response' was missing", result.Message);
    }

    [Fact]
    public void max_events_assertion_message_includes_actual_and_limit_counts()
    {
        var trace = WriteTrace(
            """
            {"event":"model_call"}
            {"event":"tool_call","tool":"x"}
            {"event":"final_response"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["max-events:2"]);
        var result = Assert.Single(report.Assertions);
        Assert.False(result.Passed);
        Assert.Equal("trace contains 3 event(s); maximum is 2", result.Message);
    }

    [Fact]
    public void no_text_assertion_pass_message_quotes_text_when_not_found()
    {
        var trace = WriteTrace("""{"event":"chat","summary":"all clean"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["no-text:TODO"]);
        var result = Assert.Single(report.Assertions);
        Assert.True(result.Passed);
        Assert.Equal("text 'TODO' was not found", result.Message);
    }

    [Fact]
    public void no_text_assertion_fail_message_quotes_text_when_found()
    {
        var trace = WriteTrace("""{"event":"chat","summary":"has TODO inside"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["no-text:TODO"]);
        var result = Assert.Single(report.Assertions);
        Assert.False(result.Passed);
        Assert.Equal("text 'TODO' was found", result.Message);
    }

    [Fact]
    public void no_text_assertion_fails_when_any_event_in_multi_event_trace_contains_text()
    {
        // Kills L191 Linq Any→All mutation: with All, the assertion would pass when only one of
        // two events contained the banned text. With Any, presence in any single event fails.
        var trace = WriteTrace(
            """
            {"event":"chat","summary":"clean"}
            {"event":"chat","summary":"has TODO marker"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["no-text:TODO"]);
        var result = Assert.Single(report.Assertions);
        Assert.False(result.Passed);
    }

    [Fact]
    public void whitespace_only_assertion_returns_ignored_pass()
    {
        var trace = WriteTrace("""{"event":"chat","summary":"hi"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], ["   "]);
        var result = Assert.Single(report.Assertions);
        Assert.True(result.Passed);
        Assert.Equal("empty assertion ignored", result.Message);
    }

    // ─── file reference extraction ────────────────────────────────────────────

    [Fact]
    public void non_file_properties_are_not_treated_as_file_references()
    {
        // Kills L280 logical mutation (&&→||) in FindFileReferences: with ||, every string would be
        // yielded as a file, polluting evt.Files for events that have no file/path properties.
        var trace = WriteTrace("""{"event":"chat","summary":"plain text"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Empty(Assert.Single(report.Events).Files);
    }

    [Fact]
    public void file_references_within_object_nested_under_file_like_property_are_captured()
    {
        // Inherits fileLikeContext into nested objects.
        var trace = WriteTrace("""{"event":"tool_call","output":{"path":"src/A.cs","other":"B"},"summary":"x"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal(["src/A.cs"], Assert.Single(report.Events).Files);
    }

    [Fact]
    public void non_file_like_string_array_is_not_treated_as_file_references()
    {
        // Kills L289 logical && → || mutation in FindFileReferences's String recursion branch.
        // With ||, any string inside a non-file-like array would be yielded as a file.
        var trace = WriteTrace("""{"event":"chat","tags":["alpha","beta"],"summary":"x"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Empty(Assert.Single(report.Events).Files);
    }

    // ─── trace loading edge cases ─────────────────────────────────────────────

    [Fact]
    public void analyze_throws_file_not_found_when_trace_file_missing()
    {
        var missing = new FileInfo(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jsonl"));
        Assert.False(missing.Exists);

        Assert.Throws<FileNotFoundException>(() =>
            DebugTraceAnalyzerService.Analyze(missing, 1, 100, [], []));
    }

    [Fact]
    public void jsonl_loader_skips_blank_lines()
    {
        var trace = WriteTrace(
            "{\"event\":\"first\"}\n\n   \n{\"event\":\"second\"}\n");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal(["first", "second"], report.Events.Select(e => e.EventType));
    }

    [Fact]
    public void empty_trace_file_yields_no_events()
    {
        var trace = WriteTrace("   ");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Empty(report.Events);
        Assert.Equal(0, report.EventCount);
    }

    // ─── report metadata ──────────────────────────────────────────────────────

    [Fact]
    public void report_includes_replay_command_with_from_step()
    {
        var trace = WriteTrace("""{"event":"chat","summary":"hi"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 3, 100, [], []);
        Assert.Equal($"rt analyze debug \"{trace.FullName}\" --from 3", report.ReplayCommand);
    }

    [Fact]
    public void event_breakpoint_hit_flag_is_false_when_no_breakpoints_match()
    {
        // Kills L120 boolean mutation: default BreakpointHit constructor argument is false.
        var trace = WriteTrace("""{"event":"chat","summary":"hi"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.False(Assert.Single(report.Events).BreakpointHit);
    }

    [Fact]
    public void trace_id_string_field_is_captured_verbatim()
    {
        var trace = WriteTrace("""{"id":"abc-123","event":"chat"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("abc-123", Assert.Single(report.Events).TraceId);
    }

    [Fact]
    public void trace_id_is_null_when_id_field_is_not_string_or_number()
    {
        var trace = WriteTrace("""{"id":true,"event":"chat"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Null(Assert.Single(report.Events).TraceId);
    }

    [Fact]
    public void trace_id_numeric_field_is_captured_as_decimal_string()
    {
        var trace = WriteTrace("""{"id":42,"event":"chat"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 100, [], []);
        Assert.Equal("42", Assert.Single(report.Events).TraceId);
    }

    // ─── limit / fromEvent boundaries ────────────────────────────────────────

    [Fact]
    public void limit_zero_means_unlimited_events_are_kept()
    {
        // Kills L27 conditional + equality mutations (limit <= 0 ? int.MaxValue : limit).
        var trace = WriteTrace(
            """
            {"event":"a"}
            {"event":"b"}
            {"event":"c"}
            {"event":"d"}
            {"event":"e"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 0, [], []);
        Assert.Equal(5, report.Events.Count);
    }

    [Fact]
    public void limit_three_caps_the_kept_events_after_from()
    {
        var trace = WriteTrace(
            """
            {"event":"a"}
            {"event":"b"}
            {"event":"c"}
            {"event":"d"}
            {"event":"e"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 1, 3, [], []);
        Assert.Equal(["a", "b", "c"], report.Events.Select(e => e.EventType));
    }

    [Fact]
    public void from_event_zero_or_negative_clamps_to_one_in_report_metadata()
    {
        // Kills L51, L55 Math.Max→Math.Min mutations: with Min, FromEvent would be 0/-1 instead of 1.
        var trace = WriteTrace("""{"event":"a"}""");
        var report = DebugTraceAnalyzerService.Analyze(trace, 0, 100, [], []);
        Assert.Equal(1, report.FromEvent);
        Assert.EndsWith("--from 1", report.ReplayCommand);
    }

    [Fact]
    public void from_event_greater_than_one_is_preserved_in_report_metadata()
    {
        // Kills L51, L55 Math.Max→Math.Min mutations from the other side: Min(1, 3)=1, Max(1, 3)=3.
        var trace = WriteTrace(
            """
            {"event":"a"}
            {"event":"b"}
            {"event":"c"}
            """);
        var report = DebugTraceAnalyzerService.Analyze(trace, 3, 100, [], []);
        Assert.Equal(3, report.FromEvent);
        Assert.EndsWith("--from 3", report.ReplayCommand);
    }

    [Fact]
    public void analyze_throws_file_not_found_with_path_and_message()
    {
        var missing = new FileInfo(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jsonl"));

        var ex = Assert.Throws<FileNotFoundException>(() =>
            DebugTraceAnalyzerService.Analyze(missing, 1, 100, [], []));

        Assert.Equal(missing.FullName, ex.FileName);
        Assert.Contains("LLM trace file was not found", ex.Message);
    }

    private static FileInfo WriteTrace(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, contents);
        return new FileInfo(path);
    }
}
