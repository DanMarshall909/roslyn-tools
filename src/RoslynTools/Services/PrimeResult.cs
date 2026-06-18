using System.Text.Json.Serialization;

namespace RoslynTools.Services;

/// <summary>
///     Compact session-start summary for <c>rt prime</c>: no MSBuild, no solution load.
///     JSON mode targets &lt; 400 tokens.
/// </summary>
internal sealed record PrimeResult(
    [property: JsonPropertyName("gate")] string Gate,
    [property: JsonPropertyName("entry")] string Entry,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("hotCommands")] IReadOnlyList<string> HotCommands);
