using System.Text.Json.Serialization;

namespace RoslynTools.Services.Suggest;

[JsonConverter(typeof(JsonStringEnumConverter<SuggestOutcome>))]
internal enum SuggestOutcome
{
    Recommendations,
    NoMatch,
    Unsupported
}

/// <summary>
///     Service-level reply from <see cref="SuggestService.Suggest" />.
///     <see cref="Related" /> carries the top result's see-also neighbours so a
///     caller doesn't have to walk the catalog a second time.
/// </summary>
internal sealed record SuggestResponse(
    IReadOnlyList<SuggestResult> Results,
    IReadOnlyList<SuggestResult> Related,
    SuggestOutcome Outcome,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MissingCapability = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SuggestWorkflow? Workflow = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? UnsupportedReason = null);
