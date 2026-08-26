using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class DebugCommand : IRtCommand, IStructuredOutputCommand, ITraceAnalyzeCommand
{
    private readonly DebugOperation _operation;

    public DebugCommand(DebugOperation operation)
    {
        _operation = operation;
    }

    public static RtCommandInfo Info { get; } = new(
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

    public Command Build()
    {
        var traceArg = new Argument<string>("trace", "Path to a JSONL trace or JSON array of LLM debugger events");
        var fromOpt = new Option<int>("--from", () => 1, "First event index to display");
        var limitOpt = new Option<int>("--limit", () => 100, "Maximum number of events to display; use 0 for all events");
        var breakOnlyOpt = new Option<bool>("--break-only", "Display only events that match a breakpoint");
        var breakOpt = new Option<string[]>("--break", "Breakpoint expression: event:<type>, tool:<name>, text:<value>, or file:<value>")
        {
            AllowMultipleArgumentsPerToken = false
        };
        var assertOpt = new Option<string[]>("--assert", "Assertion expression: no-tool:<name>, requires-event:<type>, max-events:<n>, or no-text:<value>")
        {
            AllowMultipleArgumentsPerToken = false
        };

        var cmd = new Command("debug", "Inspect a recorded LLM trace timeline with breakpoints and assertions")
        {
            traceArg,
            fromOpt,
            limitOpt,
            breakOnlyOpt,
            breakOpt,
            assertOpt
        };

        cmd.SetHandler(async ctx =>
        {
            var trace = ctx.ParseResult.GetValueForArgument(traceArg);
            var from = ctx.ParseResult.GetValueForOption(fromOpt);
            var limit = ctx.ParseResult.GetValueForOption(limitOpt);
            var breakOnly = ctx.ParseResult.GetValueForOption(breakOnlyOpt);
            var breakpoints = ctx.ParseResult.GetValueForOption(breakOpt) ?? [];
            var assertions = ctx.ParseResult.GetValueForOption(assertOpt) ?? [];

            var result = await _operation.ExecuteAsync(new DebugRequest(
                trace,
                from,
                limit,
                breakpoints,
                assertions,
                breakOnly));

            result.Match(
                report =>
                {
                    RtResultRenderer.Write(RtResult<DebugResult>.Ok(DebugResult.FromReport(report)));

                    if (report.Assertions.Any(assertion => !assertion.Passed))
                        ctx.ExitCode = 2;
                },
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }
}
