using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;

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
        FeedbackProcessOperation processOperation)
    {
        var cmd = new Command("feedback", "Log and list feedback items for rt improvement");
        cmd.AddCommand(BuildAdd(addOperation));
        cmd.AddCommand(BuildList(listOperation));
        cmd.AddCommand(BuildProcess(processOperation));
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
}
