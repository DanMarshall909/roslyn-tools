namespace RoslynTools.Services.Suggest;

/// <summary>
///     Service-level reply from <see cref="SuggestService.Suggest" />.
///     <see cref="Related" /> carries the top result's see-also neighbours so a
///     caller doesn't have to walk the catalog a second time.
/// </summary>
internal sealed record SuggestResponse(
    IReadOnlyList<SuggestResult> Results,
    IReadOnlyList<SuggestResult> Related);
