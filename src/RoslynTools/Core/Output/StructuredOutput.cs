using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoslynTools.Core.Output;

/// <summary>
///     Writes a structured object as JSON or YAML to stdout, dispatching on <see cref="RtContext.Format" />.
///     Use this in commands that don't go through <see cref="RtSerializer" /> (i.e. plain anonymous-object
///     or non-IRtOutput results) so <c>--yaml</c> works uniformly across the CLI.
/// </summary>
internal static class StructuredOutput
{
    // CamelCase keeps typed-record YAML keys (e.g. CompactDescription) in parity with their
    // [JsonPropertyName] JSON keys. Dictionary keys are emitted verbatim regardless of this
    // convention, so dict-based callers are unaffected; IRtOutput results use RtSerializer, not this.
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    // Belt-and-suspenders: escape any raw control chars that slipped past the encoder
    // (e.g. encoding-corrupted source files whose XML docs contain 0x00-0x1F bytes).
    private static readonly Regex ControlCharRegex =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F]", RegexOptions.Compiled);

    /// <summary>
    ///     Writes <paramref name="data" /> as JSON or YAML, dispatching on <see cref="RtContext.Format" />.
    ///     JSON output uses the unified encoder helper (<see cref="JsonEncoders.Select" />) so the
    ///     <c>--relax-json</c> flag toggles escaping consistently with <see cref="Renderers.JsonRenderer" />
    ///     and the native MCP handlers. The <paramref name="jsonOptions" /> parameter is preserved
    ///     in the signature for caller compatibility but is ignored for encoder selection — only the
    ///     <see cref="JsonSerializerOptions.WriteIndented" /> shape is implicit in the helper.
    /// </summary>
    internal static void Write(object data, JsonSerializerOptions jsonOptions)
    {
        if (RtContext.Format == OutputFormat.Yaml)
        {
            RtConsole.Write(RtOutputContext.Result, YamlSerializer.Serialize(data));
            return;
        }

        _ = jsonOptions; // legacy parameter; encoder is now centralised in JsonEncoders.
        var opts = JsonEncoders.Select(RtContext.RelaxJson);
        var json = JsonSerializer.Serialize(data, opts);
        json = ControlCharRegex.Replace(json, m => $"\\u{(int)m.Value[0]:x4}");
        RtConsole.WriteLine(RtOutputContext.Result, json);
    }
}
