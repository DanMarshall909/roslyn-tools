namespace RoslynTools.Core;

/// <summary>
///     Damerau-Levenshtein "did you mean" ranking for short identifier names. Used by
///     <see cref="SymbolLocator" /> drill-in not-found paths (US-135 Batch 2) to append a
///     "Did you mean: ..." tail to errors when one or more candidate names are within a
///     length-relative edit-distance threshold of the user's target.
/// </summary>
/// <remarks>
///     <para>
///         Distance is computed case-insensitively (so <c>Calclate</c> matches <c>Calculate</c>
///         even with different capitalisation), but the returned candidate strings preserve
///         the original casing from the input list — those are the names the user will type
///         next, so they must round-trip exactly.
///     </para>
///     <para>
///         Threshold: <c>min(target.Length / 2, 5)</c>. The cap of 5 prevents wildly unrelated
///         long names ("FindReferencesAsync" vs "GetDocumentLineCount") from being lumped in;
///         the proportional floor avoids the classic "Did you mean: Foo for Bar" failure
///         where every short word is within reach (len-3 target → cap 1, so dist ≥ 2 are
///         filtered out). A candidate qualifies when distance ≤ threshold.
///     </para>
/// </remarks>
internal static class NameSuggest
{
    /// <summary>
    ///     Returns up to <paramref name="max" /> candidates from <paramref name="candidates" />
    ///     ranked by Damerau-Levenshtein distance to <paramref name="target" />, ascending.
    ///     Only candidates within the length-relative threshold are returned; an empty
    ///     enumerable means "no close match — caller should omit the suggestion tail".
    /// </summary>
    public static IReadOnlyList<string> NearestNames(string target, IEnumerable<string> candidates, int max)
    {
        if (string.IsNullOrEmpty(target) || max <= 0)
            return [];

        var threshold = Math.Min(target.Length / 2, 5);
        if (threshold <= 0)
            return [];

        // Deduplicate names (overloads land here with the same name) before ranking so we
        // don't return "Did you mean: Foo, Foo, Foo".
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ranked = new List<(string Name, int Dist)>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            if (!seen.Add(candidate)) continue;
            var dist = DamerauLevenshtein(target, candidate);
            if (dist <= threshold)
                ranked.Add((candidate, dist));
        }

        return ranked
            .OrderBy(p => p.Dist)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .Take(max)
            .Select(p => p.Name)
            .ToList();
    }

    /// <summary>
    ///     Damerau-Levenshtein distance with adjacent-transposition support. Case-insensitive
    ///     so a one-character capitalisation difference doesn't burn a unit of the budget.
    /// </summary>
    private static int DamerauLevenshtein(string a, string b)
    {
        var la = a.Length;
        var lb = b.Length;
        if (la == 0) return lb;
        if (lb == 0) return la;

        var d = new int[la + 1, lb + 1];
        for (var i = 0; i <= la; i++) d[i, 0] = i;
        for (var j = 0; j <= lb; j++) d[0, j] = j;

        for (var i = 1; i <= la; i++)
        {
            for (var j = 1; j <= lb; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                var deletion = d[i - 1, j] + 1;
                var insertion = d[i, j - 1] + 1;
                var substitution = d[i - 1, j - 1] + cost;
                d[i, j] = Math.Min(deletion, Math.Min(insertion, substitution));

                if (i > 1 && j > 1
                    && char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 2])
                    && char.ToLowerInvariant(a[i - 2]) == char.ToLowerInvariant(b[j - 1]))
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[la, lb];
    }
}
