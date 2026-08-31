namespace RoslynTools.Core.CommandDescriptors;

/// <summary>
///     One application command leaf. Hosts project this metadata into their own transport
///     surfaces without carrying command-builder or parser types in the shared contract.
/// </summary>
internal sealed record ApplicationCommandDescriptor(
    IReadOnlyList<string> Path,
    string ToolName,
    string Description,
    string WhenToUse,
    ApplicationCommandInput Input,
    bool McpVisible,
    CommandSafetyPolicy Safety,
    bool Structured,
    ICommandApplicationPort? Port,
    ApplicationCommandDefinition? Definition = null,
    CommandEffects Effects = CommandEffects.RepositoryRead,
    CommandAudience Audience = CommandAudience.Public);

/// <summary>
///     Transport-neutral input metadata projected from a host command surface.
/// </summary>
internal sealed record ApplicationCommandInput(
    IReadOnlyList<ApplicationCommandParameter> Arguments,
    IReadOnlyList<ApplicationCommandParameter> Options);

/// <summary>
///     Describes one named or positional application input without exposing a CLI parser type.
/// </summary>
internal sealed record ApplicationCommandParameter(
    string Name,
    string ValueType,
    bool Required,
    string? Description = null);
