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
}
