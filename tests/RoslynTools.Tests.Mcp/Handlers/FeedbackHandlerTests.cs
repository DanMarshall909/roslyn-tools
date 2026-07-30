using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Feedback;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class FeedbackHandlerTests
{
    [Fact]
    public void feedback_tools_are_registered_for_mcp_discovery()
    {
        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();

        var tools = registry.GetTools();

        tools.Select(tool => tool.name).ShouldContain("feedback_add");
        tools.Select(tool => tool.name).ShouldContain("feedback_list");

        var add = tools.Single(tool => tool.name == "feedback_add");
        var required = (string[])add.inputSchema["required"];
        required.ShouldBe(["description"]);
        var properties =
            (Dictionary<string, Dictionary<string, object>>)add.inputSchema["properties"];
        properties.Keys.ShouldBe(
            ["description", "sampleCode", "expectedResult", "commandLine"],
            ignoreOrder: true);

        var list = tools.Single(tool => tool.name == "feedback_list");
        list.inputSchema.ShouldNotContainKey("required");
        var listProperties =
            (Dictionary<string, Dictionary<string, object>>)list.inputSchema["properties"];
        listProperties.Keys.ShouldBe(["status"]);
    }

    [Fact]
    public async Task feedback_add_records_every_input_and_returns_the_allocated_id()
    {
        FeedbackEntry? recorded = null;
        var operation = new FeedbackAddOperation((description, sampleCode, expectedResult, commandLine) =>
        {
            recorded = new FeedbackEntry
            {
                Id = "FB-374",
                Type = "user-feedback",
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Description = description,
                SampleCode = sampleCode,
                ExpectedResult = expectedResult,
                CommandLine = commandLine
            };
            return recorded;
        });
        var result = await operation.ExecuteAsync(new FeedbackAddRequest(
            "Expose feedback through MCP",
            "sample",
            "structured result",
            "feedback_add"));

        recorded.ShouldNotBeNull();
        recorded.Type.ShouldBe("user-feedback");
        recorded.Status.ShouldBe("pending");
        recorded.Description.ShouldBe("Expose feedback through MCP");
        recorded.SampleCode.ShouldBe("sample");
        recorded.ExpectedResult.ShouldBe("structured result");
        recorded.CommandLine.ShouldBe("feedback_add");
        DateTimeOffset.Parse(recorded.Timestamp, CultureInfo.InvariantCulture)
            .ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);

        result.Id.ShouldBe("FB-374");
        result.Status.ShouldBe("pending");
    }

    [Fact]
    public async Task feedback_list_returns_only_entries_matching_the_requested_status()
    {
        var entries = new List<FeedbackEntry>
        {
            new() { Id = "FB-001", Description = "pending", Status = "pending" },
            new() { Id = "FB-002", Description = "done", Status = "processed" }
        };
        var operation = new FeedbackListOperation(status => entries
            .Where(entry => string.Equals(entry.Status, status, StringComparison.OrdinalIgnoreCase))
            .ToList());
        var result = await operation.ExecuteAsync(new FeedbackListRequest("processed"));

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe("FB-002");
        result[0].Status.ShouldBe("processed");
    }
}
