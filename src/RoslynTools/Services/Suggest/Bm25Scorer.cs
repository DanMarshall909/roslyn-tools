using RoslynTools.Core;

namespace RoslynTools.Services.Suggest;

/// <summary>
///     BM25 ranker over <see cref="RtCommandInfo" /> metadata. Powers <c>rt suggest</c> and the
///     unknown-command "did you mean..." fallback. Three fields contribute (name, when-to-use,
///     examples) with descending weight; the name field can be expanded via a synonym map so
///     common LLM phrasings (e.g. "remove" → safe-delete) still match. Thin wrapper over the
///     corpus-agnostic <see cref="Bm25Core" />.
/// </summary>
/// <remarks>
///     Stateless static API rather than an injected service so callers don't have to plumb DI
///     through middleware. The scorer is host-agnostic and small enough that a static surface
///     is honest.
/// </remarks>
internal static class Bm25Scorer
{
    private const double NameWeight = 3.0;
    private const double WhenToUseWeight = 1.0;
    private const double ExamplesWeight = 0.3;

    /// <summary>
    ///     Score each entry in <paramref name="catalog" /> against <paramref name="query" /> and
    ///     return up to <paramref name="top" /> hits in descending score order.
    /// </summary>
    public static IReadOnlyList<ScoredCommand> Score(
        IReadOnlyList<RtCommandInfo> catalog,
        string query,
        int top,
        IReadOnlyDictionary<string, string[]>? nameSynonyms = null)
    {
        var results = Bm25Core.Score(
            catalog,
            info =>
            (IReadOnlyList<Bm25Core.WeightedField>)
            [
                new Bm25Core.WeightedField(ExpandName(info.Name, nameSynonyms), NameWeight),
                new Bm25Core.WeightedField(info.WhenToUse, WhenToUseWeight),
                new Bm25Core.WeightedField(string.Join(' ', info.Examples), ExamplesWeight)
            ],
            query,
            top);

        return results.Select(r => new ScoredCommand(r.Item, r.Score)).ToList();
    }

    /// <summary>
    ///     Joining synonym alternatives with a space and letting <see cref="Bm25Core" /> tokenize
    ///     the result is equivalent to tokenizing each alternative separately (whitespace is
    ///     already a token boundary), so this stays a plain string.
    /// </summary>
    private static string ExpandName(string name, IReadOnlyDictionary<string, string[]>? synonyms) =>
        synonyms is not null && synonyms.TryGetValue(name, out var alts)
            ? string.Join(' ', alts)
            : name;
}

/// <summary>
///     A single ranked result from <see cref="Bm25Scorer.Score" />.
/// </summary>
/// <param name="Command">The command metadata.</param>
/// <param name="Score">BM25 score (higher = better match).</param>
internal sealed record ScoredCommand(RtCommandInfo Command, double Score);
