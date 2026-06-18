using System.Text.Json.Serialization;

namespace RoslynTools.Services;

/// <summary>
///     The default compact shape for <c>rt inspect outline</c> per top-level type (US-135):
///     the type name, its kind, and one canonical signature per member. Carries no per-member line
///     ranges so the output stays small and stable across edits. The legacy verbose shape (grouped
///     members with line ranges) is restored with <c>--with-locations</c>; <c>--summary</c> is unchanged.
/// </summary>
internal sealed record CompactOutlineType(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("signatures")] IReadOnlyList<string> Signatures);
