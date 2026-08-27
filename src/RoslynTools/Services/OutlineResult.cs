using System.Text.Json.Serialization;

namespace RoslynTools.Services;

/// <summary>Typed outline graph shared by CLI, daemon, and MCP adapters.</summary>
internal sealed record OutlineResult(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<OutlineTypeResult>? Types,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<OutlineSummaryResult>? Summaries);

/// <summary>One summary-mode outline entry.</summary>
internal sealed record OutlineSummaryResult(
    string Fqn,
    string Kind,
    int LineFrom,
    int LineTo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Description);
