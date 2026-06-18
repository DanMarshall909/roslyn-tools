using System.Text.Json.Serialization;

namespace RoslynTools.Services;

/// <summary>
///     Unified result shape for <c>rt inspect outline</c> per top-level type (US-135 polish).
///     Replaces the prior <c>CompactOutlineType</c> + <see cref="System.Collections.Generic.Dictionary{TKey,TValue}" />-of-string-to-object verbose
///     pair with a single typed record. The compact default populates only
///     <see cref="Name" />, <see cref="Kind" />, and <see cref="Signatures" />; the verbose
///     shape (<c>--with-locations</c>) populates the remaining fields and leaves
///     <see cref="Signatures" /> null.
///     <para>
///         Property declaration order is the JSON property order. With
///         <c>WhenWritingNull</c> applied to each nullable, the compact path serializes as
///         <c>{ name, kind, signatures }</c> and the verbose path as
///         <c>{ name, fqn, kind, accessibility, lineFrom, lineTo, [baseType], [interfaces], members }</c>
///         — byte-identical to the pre-unification output.
///     </para>
/// </summary>
internal sealed record OutlineTypeResult(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("fqn"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Fqn,
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("signatures"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Signatures,
    [property: JsonPropertyName("accessibility"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Accessibility,
    [property: JsonPropertyName("lineFrom"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? LineFrom,
    [property: JsonPropertyName("lineTo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? LineTo,
    [property: JsonPropertyName("baseType"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BaseType,
    [property: JsonPropertyName("interfaces"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Interfaces,
    [property: JsonPropertyName("members"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, object?>? Members);
