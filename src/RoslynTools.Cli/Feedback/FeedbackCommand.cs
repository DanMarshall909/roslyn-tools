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

    internal static Command Build()
    {
        var cmd = new Command("feedback", "Log and list feedback items for rt improvement");
        cmd.AddCommand(BuildAdd());
        cmd.AddCommand(BuildList());
        return cmd;
    }

    private static Command BuildAdd()
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

        cmd.SetHandler((description, sampleCode, expectedResult, commandLine) =>
        {
            var existing = FeedbackStore.LoadAll();
            var entry = new FeedbackEntry
            {
                Id = FeedbackStore.NextId(existing),
                Type = "user-feedback",
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                CommandLine = commandLine,
                Description = description,
                SampleCode = sampleCode,
                ExpectedResult = expectedResult
            };

            var id = FeedbackStore.Append(entry);
            RtConsole.WriteLine(RtOutputContext.Result, $"Feedback recorded: {id}");
        }, descriptionOpt, sampleCodeOpt, expectedResultOpt, commandLineOpt);

        return cmd;
    }

    private static Command BuildList()
    {
        var statusOpt = new Option<string>("--status", () => "pending",
            "Filter by status: pending, processed, or all");

        var cmd = new Command("list", "List feedback items");
        cmd.AddOption(statusOpt);

        cmd.SetHandler(status =>
        {
            var all = FeedbackStore.LoadAll();
            var filtered = status switch
            {
                "all" => all,
                "processed" => all.Where(e =>
                    string.Equals(e.Status, "processed", StringComparison.OrdinalIgnoreCase)).ToList(),
                _ => all.Where(e =>
                    string.Equals(e.Status, "pending", StringComparison.OrdinalIgnoreCase)).ToList()
            };

            RtConsole.WriteLine(RtOutputContext.Result, JsonSerializer.Serialize(filtered, JsonOpts));
        }, statusOpt);

        return cmd;
    }
}
