using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using RoslynTools.Core.Output;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoslynTools.Core.CommandSurface;

/// <summary>
///     Projects the discovered command registry into a serializable <see cref="CommandSurfaceModel" />.
///     Pure and host-agnostic: callers pass the <c>(Command, RtCommandInfo)</c> pairs plus the group
///     descriptions and global options, so this can be unit-tested with a synthetic registry.
/// </summary>
internal static class CommandSurfaceBuilder
{
    /// <summary>camelCase, indented, null-omitting JSON options whose output matches the schema.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions(JavaScriptEncoder.Create(UnicodeRanges.All));

    /// <summary>As <see cref="JsonOptions" /> but escapes all non-ASCII as <c>\uXXXX</c> (for <c>--strict-json</c>).</summary>
    internal static readonly JsonSerializerOptions StrictJsonOptions = CreateJsonOptions(JavaScriptEncoder.Default);

    /// <summary>
    ///     Builds the command-surface snapshot from a discovered registry.
    /// </summary>
    internal static CommandSurfaceModel Build(
        IReadOnlyList<(Command Cmd, RtCommandInfo Info)> registry,
        IReadOnlyDictionary<string, string> groupDescriptions,
        IReadOnlyList<Option> globalOptions)
    {
        var commands = registry
            .OrderBy(e => e.Info.Name, StringComparer.Ordinal)
            .Select(e => Describe(e.Cmd, e.Info))
            .ToList();

        return new CommandSurfaceModel(
            commands.Count,
            commands,
            globalOptions.Select(Describe).ToList(),
            groupDescriptions);
    }

    private static CommandDescriptor Describe(Command cmd, RtCommandInfo info) => new(
        info.Name,
        info.Category,
        info.Mutates,
        info.JsonOutput,
        info.WhenToUse,
        cmd.Description ?? string.Empty,
        info.Examples,
        cmd.Options.Select(Describe).ToList(),
        cmd.Arguments.Select(Describe).ToList(),
        info.Group,
        info.Aliases,
        info.SeeAlso,
        info.KnownIssues,
        info.SupportedFormats?.Select(f => f.ToString()).ToList());

    private static OptionDescriptor Describe(Option option) => new(
        option.Name,
        option.Aliases.OrderBy(a => a, StringComparer.Ordinal).ToList(),
        option.IsRequired,
        FriendlyTypeName(option.ValueType),
        string.IsNullOrEmpty(option.Description) ? null : option.Description);

    private static ArgumentDescriptor Describe(Argument argument) => new(
        argument.Name,
        argument.Arity.MinimumNumberOfValues > 0,
        FriendlyTypeName(argument.ValueType),
        string.IsNullOrEmpty(argument.Description) ? null : argument.Description);

    /// <summary>Maps a CLR option/argument value type to a short, human-readable name.</summary>
    private static string FriendlyTypeName(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(bool)) return "bool";
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(long)) return "long";
        if (t == typeof(FileInfo)) return "file";
        if (t == typeof(DirectoryInfo)) return "directory";
        if (t.IsArray) return FriendlyTypeName(t.GetElementType()!) + "[]";
        return t.Name;
    }

    /// <summary>Serializes the model to JSON, honouring <see cref="RtContext.StrictJson" />.</summary>
    internal static string ToJson(CommandSurfaceModel model) =>
        JsonSerializer.Serialize(model, RtContext.StrictJson ? StrictJsonOptions : JsonOptions);

    /// <summary>Serializes the model to camelCase YAML, omitting null optionals to match the JSON shape.</summary>
    internal static string ToYaml(CommandSurfaceModel model) =>
        new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build()
            .Serialize(model);

    private static JsonSerializerOptions CreateJsonOptions(JavaScriptEncoder encoder) => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = encoder
    };
}
