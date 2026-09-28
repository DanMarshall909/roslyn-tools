using System.CommandLine;
using System.Text.Json;
using RoslynTools.Core.Output;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoslynTools.Core.CommandSurface;

/// <summary>
///     Projects the discovered CLI command registry into a serializable
///     <see cref="CommandSurfaceModel" />. Callers pass the
///     <c>(Command, RtCommandInfo)</c> pairs plus group descriptions and global options,
///     so this CLI projection can be tested with a synthetic registry.
/// </summary>
internal static class CommandSurfaceBuilder
{
    /// <summary>
    ///     Builds the command-surface snapshot from a discovered registry.
    /// </summary>
    internal static CommandSurfaceModel Build(
        IReadOnlyList<(Command Cmd, RtCommandInfo Info)> registry,
        IReadOnlyDictionary<string, string> groupDescriptions,
        IReadOnlyList<Option> globalOptions,
        IReadOnlyDictionary<string, CommandProductMetadata>? products = null)
    {
        var commands = registry
            .OrderBy(e => e.Info.Name, StringComparer.Ordinal)
            .Select(e =>
            {
                var product = products?.GetValueOrDefault(e.Info.Name)
                              ?? CommandProductMetadata.FromLegacy(
                                  e.Info,
                                  e.Info.Mutates ? CommandSafety.ReviewByDefault : CommandSafety.ReadOnly);
                return Describe(e.Cmd, e.Info, product);
            })
            .ToList();

        return new CommandSurfaceModel(
            commands.Count,
            commands,
            globalOptions.Select(Describe).ToList(),
            groupDescriptions);
    }

    private static CommandDescriptor Describe(
        Command cmd,
        RtCommandInfo info,
        CommandProductMetadata product) => new(
        info.Name,
        info.Category,
        info.Mutates,
        info.JsonOutput,
        info.WhenToUse,
        cmd.Description ?? string.Empty,
        info.Examples,
        cmd.Options.Select(Describe).ToList(),
        cmd.Arguments.Select(Describe).ToList(),
        Describe(product),
        info.Placement == CommandPlacement.RootOnly ? "root" : info.Group,
        info.Aliases,
        info.SeeAlso,
        info.KnownIssues,
        info.SupportedFormats?.Select(f => f.ToString()).ToList());

    private static CommandProductDescriptor Describe(CommandProductMetadata product) => new(
        product.Maturity.ToString(),
        Enum.GetValues<CommandEffects>()
            .Where(effect => effect != CommandEffects.None && product.Effects.HasFlag(effect))
            .Select(effect => effect.ToString())
            .ToArray(),
        product.ConditionallyMutable,
        product.Idempotency.ToString(),
        product.Audience.ToString(),
        product.TrustRequirement.ToString(),
        product.WorkspaceRequirement.ToString(),
        product.Prerequisites,
        product.Cost.ToString(),
        product.Cancellation.ToString(),
        product.Limits.DefaultTimeoutMilliseconds,
        product.Limits.MaximumItems,
        product.Limits.MaximumOutputBytes,
        product.Limitations);

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

    /// <summary>
    ///     Serializes the model with the shared strict-by-default encoder, honouring
    ///     the explicit <c>--relax-json</c> opt-out.
    /// </summary>
    internal static string ToJson(CommandSurfaceModel model) =>
        JsonSerializer.Serialize(model, CreateJsonOptions());

    /// <summary>Serializes the model to camelCase YAML, omitting null optionals to match the JSON shape.</summary>
    internal static string ToYaml(CommandSurfaceModel model) =>
        new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build()
            .Serialize(model);

    private static JsonSerializerOptions CreateJsonOptions() =>
        new(JsonEncoders.Select(RtContext.RelaxJson))
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
}
