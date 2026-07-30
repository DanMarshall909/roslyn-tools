using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Feedback;
using RoslynTools.Mcp.Handlers;
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
        var handler = new FeedbackAddHandler((description, sampleCode, expectedResult, commandLine) =>
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
        using var arguments = JsonDocument.Parse(
            """
            {
              "description": "Expose feedback through MCP",
              "sampleCode": "sample",
              "expectedResult": "structured result",
              "commandLine": "feedback_add"
            }
            """);

        var result = await handler.HandleAsync(arguments.RootElement, CancellationToken.None);

        result.isError.ShouldBeFalse();
        recorded.ShouldNotBeNull();
        recorded.Type.ShouldBe("user-feedback");
        recorded.Status.ShouldBe("pending");
        recorded.Description.ShouldBe("Expose feedback through MCP");
        recorded.SampleCode.ShouldBe("sample");
        recorded.ExpectedResult.ShouldBe("structured result");
        recorded.CommandLine.ShouldBe("feedback_add");
        DateTimeOffset.Parse(recorded.Timestamp, CultureInfo.InvariantCulture)
            .ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);

        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.GetProperty("id").GetString().ShouldBe("FB-374");
        payload.RootElement.GetProperty("status").GetString().ShouldBe("pending");
    }

    [Fact]
    public async Task feedback_list_returns_only_entries_matching_the_requested_status()
    {
        var entries = new List<FeedbackEntry>
        {
            new() { Id = "FB-001", Description = "pending", Status = "pending" },
            new() { Id = "FB-002", Description = "done", Status = "processed" }
        };
        var handler = new FeedbackListHandler(status => entries
            .Where(entry => string.Equals(entry.Status, status, StringComparison.OrdinalIgnoreCase))
            .ToList());
        using var arguments = JsonDocument.Parse("""{"status":"processed"}""");

        var result = await handler.HandleAsync(arguments.RootElement, CancellationToken.None);

        result.isError.ShouldBeFalse();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.GetArrayLength().ShouldBe(1);
        payload.RootElement[0].GetProperty("id").GetString().ShouldBe("FB-002");
        payload.RootElement[0].GetProperty("status").GetString().ShouldBe("processed");
    }
}
