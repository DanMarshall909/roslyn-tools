using System.Text.Json;
using RoslynTools.Feedback;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Mcp.Handlers;

internal sealed class FeedbackAddHandler : IMcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<string, string?, string?, string?, FeedbackEntry> _add;

    public FeedbackAddHandler()
        : this(FeedbackStore.AddUserFeedback)
    {
    }

    internal FeedbackAddHandler(Func<string, string?, string?, string?, FeedbackEntry> add)
    {
        _add = add;
    }

    public string ToolName => "feedback_add";

    public ToolDefinition Definition => new(
        ToolName,
        "Record rt dogfood feedback, a bug, a missing feature, or unexpected command behavior. "
        + "Returns the allocated feedback ID and pending status.",
        JsonSchemaBuilder.Object(
            required: ["description"],
            properties: new Dictionary<string, Dictionary<string, object>>
            {
                ["description"] = JsonSchemaBuilder.String(
                    "Description of the problem or feature request"),
                ["sampleCode"] = JsonSchemaBuilder.String(
                    "Sample C# code that reproduces the issue or demonstrates the request"),
                ["expectedResult"] = JsonSchemaBuilder.String(
                    "What the command should have returned or done"),
                ["commandLine"] = JsonSchemaBuilder.String(
                    "The rt command line that produced the unexpected result")
            }));

    public Task<ToolResult> HandleAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var description = ReadString(arguments, "description");
        if (string.IsNullOrWhiteSpace(description))
            return Task.FromResult(
                new ToolResult(true, "Missing required string property 'description'."));

        var entry = _add(
            description,
            ReadString(arguments, "sampleCode"),
            ReadString(arguments, "expectedResult"),
            ReadString(arguments, "commandLine"));
        var json = JsonSerializer.Serialize(
            new { id = entry.Id, status = entry.Status },
            JsonOptions);
        return Task.FromResult(new ToolResult(false, json));
    }

    private static string? ReadString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String)
            return null;

        var value = element.GetString();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}

internal sealed class FeedbackListHandler : IMcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<string, List<FeedbackEntry>> _list;

    public FeedbackListHandler()
        : this(FeedbackStore.ListByStatus)
    {
    }

    internal FeedbackListHandler(Func<string, List<FeedbackEntry>> list)
    {
        _list = list;
    }

    public string ToolName => "feedback_list";

    public ToolDefinition Definition => new(
        ToolName,
        "List rt feedback as structured entries, filtered by pending, processed, or all.",
        JsonSchemaBuilder.Object(
            properties: new Dictionary<string, Dictionary<string, object>>
            {
                ["status"] = JsonSchemaBuilder.String(
                    "Filter by status: pending, processed, or all (default pending)")
            }));

    public Task<ToolResult> HandleAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = ReadString(arguments, "status") ?? "pending";
        var json = JsonSerializer.Serialize(_list(status), JsonOptions);
        return Task.FromResult(new ToolResult(false, json));
    }

    private static string? ReadString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String)
            return null;

        var value = element.GetString();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
