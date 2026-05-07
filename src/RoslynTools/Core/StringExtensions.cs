namespace RoslynTools.Core;

internal static class StringExtensions
{
    /// <summary>Ordinal StartsWith — avoids CA1310 boilerplate.</summary>
    internal static bool StartsWithOrdinal(this string s, string value)
    {
        return s.StartsWith(value, StringComparison.Ordinal);
    }
}
