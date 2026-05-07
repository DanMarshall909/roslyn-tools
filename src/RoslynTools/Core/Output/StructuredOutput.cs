using System.Text.Json;
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

    internal static void Write(object data, JsonSerializerOptions jsonOptions)
    {
        if (RtContext.Format == OutputFormat.Yaml)
        {
            RtConsole.Write(RtOutputContext.Result, YamlSerializer.Serialize(data));
            return;
        }

        RtConsole.WriteLine(RtOutputContext.Result, JsonSerializer.Serialize(data, jsonOptions));
    }
}
