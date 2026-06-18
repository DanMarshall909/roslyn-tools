using System.Text.Json.Serialization;

namespace RoslynTools.Services;

/// <summary>
///     The default compact shape for <c>rt inspect describe &lt;FQN&gt;</c> on a named type (US-135):
///     just the fully-qualified name, the type kind, and one canonical signature per member. Carries no
///     line numbers or locations so the output stays small and stable across edits. The legacy verbose
///     shape is restored with <c>--with-locations</c>.
/// </summary>
internal sealed record CompactDescription(
    [property: JsonPropertyName("fqn")] string Fqn,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("signatures")] IReadOnlyList<string> Signatures);
