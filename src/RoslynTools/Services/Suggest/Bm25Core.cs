namespace RoslynTools.Services.Suggest;

/// <summary>Reusable, weighted multi-field BM25 ranker.</summary>
internal static class Bm25Core
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "of", "to", "in", "for", "this", "that", "all"
    };

    internal readonly record struct WeightedField(string Text, double Weight);

    internal static IReadOnlyList<(T Item, double Score)> Score<T>(
        IReadOnlyList<T> corpus,
        Func<T, IReadOnlyList<WeightedField>> fields,
        string query,
        int top)
    {
        var queryTokens = Tokenize(query)
            .Where(token => !Stopwords.Contains(token))
            .ToList();
        if (queryTokens.Count == 0 || corpus.Count == 0 || top <= 0)
            return [];

        var documents = corpus
            .Select(item => (
                Item: item,
                Fields: fields(item)
                    .Select(field => (field.Weight, Tokens: Tokenize(field.Text).ToList()))
                    .ToList()))
            .ToList();
        var fieldCount = documents[0].Fields.Count;
        if (documents.Any(document => document.Fields.Count != fieldCount))
            throw new ArgumentException("Every BM25 document must have the same number of fields.", nameof(fields));

        var averageLengths = Enumerable.Range(0, fieldCount)
            .Select(index => documents.Average(document => (double)document.Fields[index].Tokens.Count))
            .ToArray();
        var idf = ComputeIdf(queryTokens, documents, fieldCount);

        var scored = new List<(T Item, double Score)>();
        foreach (var document in documents)
        {
            double score = 0;
            for (var index = 0; index < fieldCount; index++)
                score += document.Fields[index].Weight
                         * ScoreField(
                             queryTokens,
                             document.Fields[index].Tokens,
                             averageLengths[index],
                             idf);
            if (score > 0)
                scored.Add((document.Item, score));
        }

        return scored
            .OrderByDescending(result => result.Score)
            .Take(top)
            .ToList();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var buffer = new System.Text.StringBuilder();
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Append(char.ToLowerInvariant(character));
            }
            else if (buffer.Length > 0)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
            yield return buffer.ToString();
    }

    private static Dictionary<string, double> ComputeIdf<T>(
        IReadOnlyList<string> queryTokens,
        List<(T Item, List<(double Weight, List<string> Tokens)> Fields)> documents,
        int fieldCount)
    {
        var idf = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var token in queryTokens.Distinct())
        {
            var documentFrequency = documents.Count(document =>
                Enumerable.Range(0, fieldCount)
                    .Any(index => document.Fields[index].Tokens.Contains(token)));
            idf[token] = Math.Log(
                1 + (documents.Count - documentFrequency + 0.5) / (documentFrequency + 0.5));
        }

        return idf;
    }

    private static double ScoreField(
        IReadOnlyList<string> queryTokens,
        List<string> documentTokens,
        double averageLength,
        IReadOnlyDictionary<string, double> idf)
    {
        if (documentTokens.Count == 0 || averageLength <= 0)
            return 0;

        double score = 0;
        foreach (var token in queryTokens.Distinct())
        {
            var frequency = documentTokens.Count(candidate => candidate == token);
            if (frequency == 0)
                continue;

            var numerator = frequency * (K1 + 1);
            var denominator = frequency
                              + K1 * (1 - B + B * documentTokens.Count / averageLength);
            score += idf.GetValueOrDefault(token) * numerator / denominator;
        }

        return score;
    }
}
