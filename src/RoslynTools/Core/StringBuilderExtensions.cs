using System.Globalization;
using System.Text;

namespace RoslynTools.Core;

internal static class StringBuilderExtensions
{
    /// <summary>Appends an invariant-culture interpolated line — avoids CA1305 boilerplate.</summary>
    internal static StringBuilder AppendLineF(this StringBuilder sb, FormattableString value)
    {
        sb.AppendLine(value.ToString(CultureInfo.InvariantCulture));
        return sb;
    }

    /// <summary>Appends an invariant-culture interpolated string — avoids CA1305 boilerplate.</summary>
    internal static StringBuilder AppendF(this StringBuilder sb, FormattableString value)
    {
        sb.Append(value.ToString(CultureInfo.InvariantCulture));
        return sb;
    }
}
