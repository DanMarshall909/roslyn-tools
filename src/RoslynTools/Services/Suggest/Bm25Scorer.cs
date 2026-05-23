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

    private const double K1 = 1.2;
    private const double B = 0.75;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "of", "to", "in", "for", "this", "that", "all"
    };

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
        var queryTokens = Tokenize(query)
            .Where(t => !Stopwords.Contains(t))
            .ToList();
        if (queryTokens.Count == 0 || catalog.Count == 0)
            return [];

        var fields = catalog
            .Select(info => new Fields(
                info,
                NameTokens: ExpandName(info.Name, nameSynonyms),
                WhenToUseTokens: Tokenize(info.WhenToUse).ToList(),
                ExamplesTokens: Tokenize(string.Join(' ', info.Examples)).ToList()))
            .ToList();

        double avgName = fields.Average(f => (double)f.NameTokens.Count);
        double avgWhen = fields.Average(f => (double)f.WhenToUseTokens.Count);
        double avgEx = fields.Average(f => (double)f.ExamplesTokens.Count);

        var idf = ComputeIdf(queryTokens, fields);

        var scored = new List<ScoredCommand>(fields.Count);
        foreach (var field in fields)
        {
            double score =
                NameWeight * Bm25Field(queryTokens, field.NameTokens, avgName, idf)
                + WhenToUseWeight * Bm25Field(queryTokens, field.WhenToUseTokens, avgWhen, idf)
                + ExamplesWeight * Bm25Field(queryTokens, field.ExamplesTokens, avgEx, idf);
            if (score > 0)
                scored.Add(new ScoredCommand(field.Info, score));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Count <= top ? scored : scored.GetRange(0, top);
    }

    private static List<string> ExpandName(
        string name,
        IReadOnlyDictionary<string, string[]>? synonyms)
    {
        if (synonyms is not null && synonyms.TryGetValue(name, out var alts))
            return alts.SelectMany(Tokenize).ToList();
        return Tokenize(name).ToList();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;
        var buf = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buf.Append(char.ToLowerInvariant(ch));
            }
            else if (buf.Length > 0)
            {
                yield return buf.ToString();
                buf.Clear();
            }
        }
        if (buf.Length > 0)
            yield return buf.ToString();
    }

    private static Dictionary<string, double> ComputeIdf(
        IReadOnlyList<string> queryTokens,
        IReadOnlyList<Fields> fields)
    {
        // IDF is computed once over the union of all three fields so a term that
        // appears in many commands' descriptions is dampened uniformly.
        int n = fields.Count;
        var idf = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var token in queryTokens.Distinct())
        {
            int df = fields.Count(f =>
                f.NameTokens.Contains(token) ||
                f.WhenToUseTokens.Contains(token) ||
                f.ExamplesTokens.Contains(token));
            // Standard BM25 IDF with +1 smoothing so single-doc hits stay positive.
            idf[token] = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
        }
        return idf;
    }

    private static double Bm25Field(
        IReadOnlyList<string> queryTokens,
        List<string> docTokens,
        double avgLen,
        IReadOnlyDictionary<string, double> idf)
    {
        if (docTokens.Count == 0 || avgLen <= 0)
            return 0;
        double dl = docTokens.Count;
        double score = 0;
        foreach (var qt in queryTokens.Distinct())
        {
            int tf = 0;
            for (var i = 0; i < docTokens.Count; i++)
                if (docTokens[i] == qt) tf++;
            if (tf == 0) continue;
            double weight = idf.GetValueOrDefault(qt, 0);
            double numer = tf * (K1 + 1);
            double denom = tf + K1 * (1 - B + B * dl / avgLen);
            score += weight * numer / denom;
        }
        return score;
    }

    private sealed record Fields(
        RtCommandInfo Info,
        List<string> NameTokens,
        List<string> WhenToUseTokens,
        List<string> ExamplesTokens);
}

/// <summary>
///     A single ranked result from <see cref="Bm25Scorer.Score" />.
/// </summary>
/// <param name="Command">The command metadata.</param>
/// <param name="Score">BM25 score (higher = better match).</param>
internal sealed record ScoredCommand(RtCommandInfo Command, double Score);
