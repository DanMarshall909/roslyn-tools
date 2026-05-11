using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace RoslynTools.Core.Output;

/// <summary>
///     Writes a structured object as JSON or YAML to stdout, dispatching on <see cref="RtContext.Format" />.
///     Use this in commands that don't go through <see cref="RtSerializer" /> (i.e. plain anonymous-object
///     or non-IRtOutput results) so <c>--yaml</c> works uniformly across the CLI.
/// </summary>
internal static class StructuredOutput
{
    private static readonly ISerializer YamlSerializer = new SerializerBuilder().Build();

    // Belt-and-suspenders: escape any raw control chars that slipped past the encoder
    // (e.g. encoding-corrupted source files whose XML docs contain 0x00-0x1F bytes).
    private static readonly Regex ControlCharRegex =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F]", RegexOptions.Compiled);

    /// <summary>
    /// Method to write structured output to a text writer with serialization options.
    /// </summary>
    internal static void Write(object data, JsonSerializerOptions jsonOptions)
    {
        if (RtContext.Format == OutputFormat.Yaml)
        {
            RtConsole.Write(RtOutputContext.Result, YamlSerializer.Serialize(data));
            return;
        }

        var opts = RtContext.StrictJson ? SymbolFormatter.StrictJsonOptions : jsonOptions;
        var json = JsonSerializer.Serialize(data, opts);
        json = ControlCharRegex.Replace(json, m => $"\\u{(int)m.Value[0]:x4}");
        RtConsole.WriteLine(RtOutputContext.Result, json);
    }
}
