using RoslynTools.Core;

namespace RoslynTools.Services.Suggest;

/// <summary>
///     Pure BM25 ranker over <see cref="RtCommandInfo" /> metadata. Powers
///     <c>rt suggest</c> and the unknown-command "did you mean..." fallback.
///     Three fields contribute (name, when-to-use, examples) with descending
///     weight; the name field can be expanded via a synonym map so common
///     LLM phrasings (e.g. "remove" → safe-delete) still match.
/// </summary>
/// <remarks>
///     Stateless static API rather than an injected service so callers don't
///     have to plumb DI through middleware. The scorer is host-agnostic and
///     small enough that a static surface is honest.
/// </remarks>
internal static class Bm25Scorer
{
    private const double NameWeight = 3.0;
    private const double WhenToUseWeight = 1.0;
    private const double ExamplesWeight = 0.3;

    /// <summary>
    ///     Score each entry in <paramref name="catalog" /> against
    ///     <paramref name="query" /> and return up to <paramref name="top" />
    ///     hits in descending score order. Entries with no token overlap are
    ///     dropped (score 0 is filtered).
    /// </summary>
    public static IReadOnlyList<ScoredCommand> Score(
        IReadOnlyList<RtCommandInfo> catalog,
        string query,
        int top,
        IReadOnlyDictionary<string, string[]>? nameSynonyms = null)
    {
        return Bm25Core.Score(
                catalog,
                info =>
                    (IReadOnlyList<Bm25Core.WeightedField>)
                    [
                        new(ExpandName(info.Name, nameSynonyms), NameWeight),
                        new(info.WhenToUse, WhenToUseWeight),
                        new(string.Join(' ', info.Examples), ExamplesWeight)
                    ],
                query,
                top)
            .Select(result => new ScoredCommand(result.Item, result.Score))
            .ToList();
    }

    private static string ExpandName(
        string name,
        IReadOnlyDictionary<string, string[]>? synonyms)
    {
        if (synonyms is not null && synonyms.TryGetValue(name, out var alts))
            return string.Join(' ', alts);
        return name;
    }
}

/// <summary>
///     A single ranked result from <see cref="Bm25Scorer.Score" />.
/// </summary>
/// <param name="Command">The command metadata.</param>
/// <param name="Score">BM25 score (higher = better match).</param>
internal sealed record ScoredCommand(RtCommandInfo Command, double Score);
