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
        FeedbackReportOperation? reportOperation = null)
    {
        var cmd = new Command("feedback", "Log and list feedback items for rt improvement");
        cmd.AddCommand(BuildAdd(addOperation));
        cmd.AddCommand(BuildList(listOperation));
        cmd.AddCommand(BuildProcess(processOperation));
        if (reportOperation is not null)
            cmd.AddCommand(BuildReport(reportOperation));
        return cmd;
    }

    internal static Command BuildAdd(FeedbackAddOperation operation)
    {
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

        var cmd = new Command("add", "Record a feedback item or feature request");
        cmd.AddOption(descriptionOpt);
        cmd.AddOption(sampleCodeOpt);
        cmd.AddOption(expectedResultOpt);
        cmd.AddOption(commandLineOpt);

        cmd.SetHandler(async (description, sampleCode, expectedResult, commandLine) =>
        {
            var result = await operation.ExecuteAsync(new FeedbackAddRequest(
                description,
                sampleCode,
                expectedResult,
                commandLine));
            RtConsole.WriteLine(RtOutputContext.Result, $"Feedback recorded: {result.Id}");
        }, descriptionOpt, sampleCodeOpt, expectedResultOpt, commandLineOpt);

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

    internal static Command BuildReport(FeedbackReportOperation operation)
    {
        var idOpt = new Option<string>("--id", "Feedback ID to report on GitHub")
        {
            IsRequired = true
        };
        var yesOpt = new Option<bool>("--yes", "Create a new issue after reviewing duplicates");
        var attachToOpt = new Option<int?>(
            "--attach-to",
            "Link this feedback to an existing GitHub issue instead of creating one");
        var dryRunOpt = new Option<bool>(
            "--dry-run",
            "Preview duplicate suggestions and the intended action without persisting anything");

        var cmd = new Command(
            "report",
            "Report local feedback to GitHub through gh. Requires explicit --yes or --attach-to to persist.");
        cmd.AddOption(idOpt);
        cmd.AddOption(yesOpt);
        cmd.AddOption(attachToOpt);
        cmd.AddOption(dryRunOpt);
        cmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            var result = await operation.ExecuteAsync(new FeedbackReportRequest(
                parse.GetValueForOption(idOpt),
                parse.GetValueForOption(yesOpt),
                parse.GetValueForOption(attachToOpt),
                parse.GetValueForOption(dryRunOpt)));
            result.Match(
                Render,
                error => RtErrorPresenter.Surface(context, error));
        });
        return cmd;
    }

    private static void Render(FeedbackReportResult result)
    {
        if (RtContext.Format == OutputFormat.Json)
        {
            RtConsole.WriteLine(
                RtOutputContext.Result,
                JsonSerializer.Serialize(result, JsonOpts));
            return;
        }

        switch (result.Status)
        {
            case "reported":
                RtConsole.WriteLine(
                    RtOutputContext.Result,
                    $"Filed GitHub issue #{result.IssueNumber}: {result.IssueUrl}");
                return;
            case "attached":
                RtConsole.WriteLine(
                    RtOutputContext.Result,
                    $"Linked {result.Id} to GitHub issue #{result.IssueNumber}: {result.IssueUrl}");
                return;
            case "already-reported":
                RtConsole.WriteLine(
                    RtOutputContext.Result,
                    $"{result.Id} is already linked to GitHub issue #{result.IssueNumber}: {result.IssueUrl}");
                return;
            case "dry-run":
                RtConsole.WriteLine(
                    RtOutputContext.Result,
                    $"Dry run: nothing changed; {result.PossibleDuplicates.Count} possible duplicate(s).");
                RenderDuplicates(result);
                return;
            case "duplicates-found":
                RtConsole.WriteLine(RtOutputContext.Result, "Possible duplicates found:");
                RenderDuplicates(result);
                RtConsole.WriteLine(
                    RtOutputContext.Result,
                    $"Use --attach-to <number> or `rt feedback report --id {result.Id} --yes`.");
                return;
            default:
                RtConsole.WriteLine(
                    RtOutputContext.Result,
                    $"No likely duplicates found. Re-run `rt feedback report --id {result.Id} --yes` to create the issue.");
                return;
        }
    }

    private static void RenderDuplicates(FeedbackReportResult result)
    {
        foreach (var duplicate in result.PossibleDuplicates)
            RtConsole.WriteLine(
                RtOutputContext.Result,
                $"  #{duplicate.Number} {duplicate.Title} ({duplicate.Score:F1}) {duplicate.Url}");
    }
}
