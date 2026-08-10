namespace RoslynTools.Services.Suggest;

/// <summary>
///     Generic multi-field BM25 core shared by <see cref="Bm25Scorer" /> (rt command catalog
///     ranking, powering <c>rt suggest</c>) and
///     <see cref="RoslynTools.Services.GitHub.IssueDuplicateScorer" /> (GitHub issue duplicate
///     ranking, powering <c>rt feedback report</c>). Extracted so both callers share one BM25
///     implementation instead of two copies of the same math over different corpora.
/// </summary>
internal static class Bm25Core
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "of", "to", "in", "for", "this", "that", "all"
    };

    /// <summary>One weighted text field contributed by a corpus item.</summary>
    internal readonly record struct WeightedField(string Text, double Weight);

    /// <summary>
    ///     Score each item in <paramref name="corpus" /> against <paramref name="query" /> using
    ///     per-item weighted fields from <paramref name="fields" />, returning up to
    ///     <paramref name="top" /> hits in descending score order. Items with no token overlap
    ///     are dropped (score 0 is filtered). Every item must contribute the same number of
    ///     fields, in the same order.
    /// </summary>
    internal static IReadOnlyList<(T Item, double Score)> Score<T>(
        IReadOnlyList<T> corpus,
        Func<T, IReadOnlyList<WeightedField>> fields,
        string query,
        int top)
    {
        var queryTokens = Tokenize(query)
            .Where(t => !Stopwords.Contains(t))
            .ToList();
        if (queryTokens.Count == 0 || corpus.Count == 0)
            return [];

        var docs = corpus
            .Select(item => (Item: item, Fields: fields(item)
                .Select(f => (f.Weight, Tokens: Tokenize(f.Text).ToList()))
                .ToList()))
            .ToList();

        var fieldCount = docs[0].Fields.Count;
        var avgLen = new double[fieldCount];
        for (var i = 0; i < fieldCount; i++)
            avgLen[i] = docs.Average(d => (double)d.Fields[i].Tokens.Count);

        var idf = ComputeIdf(queryTokens, docs, fieldCount);

        var scored = new List<(T, double)>(docs.Count);
        foreach (var doc in docs)
        {
            double score = 0;
            for (var i = 0; i < fieldCount; i++)
                score += doc.Fields[i].Weight * Bm25Field(queryTokens, doc.Fields[i].Tokens, avgLen[i], idf);
            if (score > 0)
                scored.Add((doc.Item, score));
        }

        scored.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return scored.Count <= top ? scored : scored.GetRange(0, top);
    }

    internal static IEnumerable<string> Tokenize(string text)
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

    private static Dictionary<string, double> ComputeIdf<T>(
        IReadOnlyList<string> queryTokens,
        List<(T Item, List<(double Weight, List<string> Tokens)> Fields)> docs,
        int fieldCount)
    {
        // IDF is computed once over the union of all fields so a term that appears in many
        // items' text is dampened uniformly.
        int n = docs.Count;
        var idf = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var token in queryTokens.Distinct())
        {
            int df = docs.Count(d =>
                Enumerable.Range(0, fieldCount).Any(i => d.Fields[i].Tokens.Contains(token)));
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
}
