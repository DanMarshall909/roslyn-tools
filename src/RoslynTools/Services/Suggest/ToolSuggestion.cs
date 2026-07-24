using System.Text.Json.Serialization;

namespace RoslynTools.Services.Suggest;

internal sealed record ToolSuggestion(
    [property: JsonPropertyName("toolName")] string ToolName,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, object?> Arguments,
    [property: JsonPropertyName("cli")] string Cli);
