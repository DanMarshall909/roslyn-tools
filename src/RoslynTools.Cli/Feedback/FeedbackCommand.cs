using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Feedback;

internal static class FeedbackCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static Command Build(
        FeedbackAddOperation addOperation,
        FeedbackListOperation listOperation,
        FeedbackProcessOperation processOperation,
        FeedbackReportOperation reportOperation)
    {
        var cmd = new Command("feedback", "Log and list feedback items for rt improvement");
        cmd.AddCommand(BuildAdd(addOperation));
        cmd.AddCommand(BuildList(listOperation));
        cmd.AddCommand(BuildProcess(processOperation));
        cmd.AddCommand(BuildReport(reportOperation));
        return cmd;
    }

    internal static Command BuildAdd(FeedbackAddOperation operation)
    {
        var titleOpt = new Option<string>("--title",
            "Short one-line summary. Becomes the GitHub issue title if this is later reported, "
            + "and is the primary duplicate-detection signal — keep it specific.")
        {
            IsRequired = true
        };
        var categoryOpt = new Option<string>("--category",
            $"One of: {string.Join(", ", FeedbackCategories.AllNames)}")
        {
            IsRequired = true
        };
        categoryOpt.FromAmong(FeedbackCategories.AllNames);
        var descriptionOpt = new Option<string>("--description", "Description of the problem or feature request")
        {
            IsRequired = true
        };
        var sampleCodeOpt = new Option<string?>("--sample-code",
            "Sample C# code to reproduce the issue or demonstrate the desired feature");
        var expectedResultOpt = new Option<string?>("--expected-result",
            "What the command should have returned or done");
        var commandLineOpt = new Option<string?>("--command-line",
            "The rt command line that produced the unexpected result");
        var commandOpt = new Option<string?>("--command",
            "Which rt command/area this concerns (e.g. 'inspect describe'). Optional, but sharpens "
            + "duplicate detection if this is later reported.");

        var cmd = new Command("add", "Record a feedback item or feature request");
        cmd.AddOption(titleOpt);
        cmd.AddOption(categoryOpt);
        cmd.AddOption(descriptionOpt);
        cmd.AddOption(sampleCodeOpt);
        cmd.AddOption(expectedResultOpt);
        cmd.AddOption(commandLineOpt);
        cmd.AddOption(commandOpt);

        cmd.SetHandler(async ctx =>
        {
            var p = ctx.ParseResult;
            var categoryRaw = p.GetValueForOption(categoryOpt);
            FeedbackCategory? category = categoryRaw is not null
                && Enum.TryParse<FeedbackCategory>(categoryRaw, ignoreCase: true, out var parsed)
                    ? parsed
                    : null;

            var result = await operation.ExecuteAsync(new FeedbackAddRequest(
                p.GetValueForOption(titleOpt),
                category,
                p.GetValueForOption(descriptionOpt),
                p.GetValueForOption(sampleCodeOpt),
                p.GetValueForOption(expectedResultOpt),
                p.GetValueForOption(commandLineOpt),
                p.GetValueForOption(commandOpt)));
            RtConsole.WriteLine(RtOutputContext.Result, $"Feedback recorded: {result.Id}");
        });

        return cmd;
    }

    internal static Command BuildList(FeedbackListOperation operation)
    {
        var statusOpt = new Option<string>("--status", () => "pending",
            "Filter by status: pending, processed, or all");

        var cmd = new Command("list", "List feedback items");
        cmd.AddOption(statusOpt);

        cmd.SetHandler(async status =>
        {
            var filtered = await operation.ExecuteAsync(new FeedbackListRequest(status));
            RtConsole.WriteLine(RtOutputContext.Result, JsonSerializer.Serialize(filtered, JsonOpts));
        }, statusOpt);

        return cmd;
    }

    internal static Command BuildProcess(FeedbackProcessOperation operation)
    {
        var idOpt = new Option<string>("--id", "Feedback ID to mark processed")
        {
            IsRequired = true
        };
        var resolutionOpt = new Option<string>("--resolution", "How the feedback item was resolved")
        {
            IsRequired = true
        };

        var cmd = new Command("process", "Mark a feedback item as processed");
        cmd.AddOption(idOpt);
        cmd.AddOption(resolutionOpt);

        cmd.SetHandler(async (id, resolution) =>
        {
            var result = await operation.ExecuteAsync(new FeedbackProcessRequest(id, resolution));
            RtConsole.WriteLine(RtOutputContext.Result, $"Feedback processed: {result.Id}");
        }, idOpt, resolutionOpt);

        return cmd;
    }

    /// <summary>
    ///     Files a pending feedback item as a public GitHub issue. This is a human-directed
    ///     action, deliberately kept separate from <c>add</c> — see the <c>feedback_report</c>
    ///     MCP descriptor for the "never call this automatically" guidance that applies equally
    ///     here.
    /// </summary>
    internal static Command BuildReport(FeedbackReportOperation operation)
    {
        var idOpt = new Option<string>("--id", "Feedback ID to report on GitHub (e.g. FB-004)")
        {
            IsRequired = true
        };
        var yesOpt = new Option<bool>("--yes",
            "File a new GitHub issue even if possible duplicates were found");
        var attachToOpt = new Option<int?>("--attach-to",
            "Link this feedback to an existing GitHub issue number instead of filing a new one");
        var dryRunOpt = new Option<bool>("--dry-run",
            "Search for duplicates and show what would be filed, without creating or attaching anything");

        var cmd = new Command("report",
            "File a pending feedback item as a public GitHub issue via the gh CLI, with duplicate "
            + "detection. Requires gh installed and authenticated (gh auth login). Only run this at "
            + "an explicit human request — never automatically.");
        cmd.AddOption(idOpt);
        cmd.AddOption(yesOpt);
        cmd.AddOption(attachToOpt);
        cmd.AddOption(dryRunOpt);

        cmd.SetHandler(async ctx =>
        {
            var p = ctx.ParseResult;
            var request = new FeedbackReportRequest(
                p.GetValueForOption(idOpt),
                p.GetValueForOption(yesOpt),
                p.GetValueForOption(attachToOpt),
                p.GetValueForOption(dryRunOpt));

            var result = await operation.ExecuteAsync(request);
            result.Match(
                success => Render(success),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private static void Render(FeedbackReportResult result)
    {
        if (RtContext.Format == OutputFormat.Json)
        {
            RtConsole.WriteLine(RtOutputContext.Result, JsonSerializer.Serialize(result, JsonOpts));
            return;
        }

        switch (result.Status)
        {
            case "reported":
                RtConsole.WriteLine(RtOutputContext.Result,
                    $"Filed GitHub issue #{result.IssueNumber}: {result.IssueUrl}");
                return;
            case "attached":
                RtConsole.WriteLine(RtOutputContext.Result,
                    $"Linked {result.Id} to existing GitHub issue #{result.IssueNumber}: {result.IssueUrl}");
                return;
            case "already-reported":
                RtConsole.WriteLine(RtOutputContext.Result,
                    $"{result.Id} was already reported as GitHub issue #{result.IssueNumber}: {result.IssueUrl}");
                return;
            case "dry-run":
                RtConsole.WriteLine(RtOutputContext.Result,
                    $"Dry run — nothing filed. {result.PossibleDuplicates.Count} possible duplicate(s) found.");
                RenderDuplicates(result);
                return;
            default: // "duplicates-found"
                RtConsole.WriteLine(RtOutputContext.Result,
                    "Possible duplicates found — review before filing a new issue:");
                RenderDuplicates(result);
                RtConsole.WriteLine(RtOutputContext.Result, "");
                RtConsole.WriteLine(RtOutputContext.Result,
                    $"None of these match? Re-run with `rt feedback report --id {result.Id} --yes` to file a "
                    + $"new issue, or `rt feedback report --id {result.Id} --attach-to <number>` to link to "
                    + "one of the issues above instead.");
                return;
        }
    }

    private static void RenderDuplicates(FeedbackReportResult result)
    {
        foreach (var candidate in result.PossibleDuplicates)
        {
            RtConsole.WriteLine(RtOutputContext.Result,
                $"  #{candidate.Number}  {candidate.Title}  (score {candidate.Score:F1})");
            RtConsole.WriteLine(RtOutputContext.Result, $"        {candidate.Url}");
        }
    }
}
