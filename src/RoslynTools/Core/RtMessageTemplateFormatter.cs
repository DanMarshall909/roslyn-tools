using System.Globalization;
using System.Text.RegularExpressions;

namespace RoslynTools.Core;

/// <summary>Formats named diagnostic placeholders without choosing an output destination.</summary>
internal static class RtMessageTemplateFormatter
{
    internal static string Render(string template, object?[] args)
    {
        if (args.Length == 0)
            return template;

        var index = 0;
        var positioned = Regex.Replace(
            template,
            @"\{([a-zA-Z_][a-zA-Z0-9_]*)(:[^{}]*)?\}",
            match => $"{{{index++}{match.Groups[2].Value}}}");

        return string.Format(CultureInfo.InvariantCulture, positioned, args);
    }
}
