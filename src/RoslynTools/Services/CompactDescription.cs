using System.Text.Json.Serialization;

namespace RoslynTools.Services;

/// <summary>
///     The default compact shape for <c>rt inspect describe &lt;FQN&gt;</c> on a named type (US-135):
///     just the fully-qualified name, the type kind, and one canonical signature per member. Carries no
///     line numbers or locations so the output stays small and stable across edits. The legacy verbose
///     shape is restored with <c>--with-locations</c>.
///     <para>
///         <see cref="RelatedSymbolFqns" /> is populated by <c>InspectService.BuildCompact</c> (not
///         serialized) and consumed by CLI/MCP adapters to build the <see cref="Suggestions" /> array.
///     </para>
/// </summary>
internal sealed record CompactDescription(
    [property: JsonPropertyName("fqn")] string Fqn,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("signatures")] IReadOnlyList<string> Signatures)
{
    /// <summary>
    ///     FQNs of solution-internal types that this type depends on (ctor params, method signatures,
    ///     properties, fields). Populated by <c>BuildCompact</c>; not emitted in JSON.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> RelatedSymbolFqns { get; init; } = [];

    /// <summary>
    ///     Ready-to-run follow-on rt commands, pre-filled with the FQN and solution path.
    ///     Populated by CLI/MCP adapters from <see cref="RelatedSymbolFqns" /> plus static routing.
    /// </summary>
    [JsonPropertyName("_suggestions")]
    public IReadOnlyList<string>? Suggestions { get; init; }
}
