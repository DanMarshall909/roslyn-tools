namespace RoslynTools.Core;

/// <summary>
/// Provides extension methods for the String class.
/// </summary>
internal static class StringExtensions
{
    /// <summary>Ordinal StartsWith — avoids CA1310 boilerplate.</summary>
    internal static bool StartsWithOrdinal(this string s, string value)
    {
        return s.StartsWith(value, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The first line of <paramref name="text" />, trimmed and ellipsis-truncated to at most
    ///     <paramref name="max" /> characters. Shared by any caller that needs a short, single-line
    ///     summary from arbitrary (possibly multi-line) text — e.g. a suggestion blurb or an
    ///     exception message used as a feedback entry's title.
    /// </summary>
    internal static string TruncateOneLine(this string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var firstLine = text.Split('\n', 2)[0].Trim();
        return firstLine.Length <= max ? firstLine : firstLine[..(max - 1)] + "…";
    }
}
