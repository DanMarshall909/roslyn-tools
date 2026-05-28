namespace RoslynTools.Core.CommandSurface;

/// <summary>
///     Serializable snapshot of the entire <c>rt</c> command surface — every command's
///     metadata plus its options and positional arguments, and the host's global options.
///     Emitted by <c>rt dump-help --json</c> / <c>--yaml</c> and described by
///     <c>docs/command-surface.schema.json</c>. Built by <see cref="CommandSurfaceBuilder" />.
/// </summary>
internal sealed record CommandSurfaceModel(
    int CommandCount,
    IReadOnlyList<CommandDescriptor> Commands,
    IReadOnlyList<OptionDescriptor> GlobalOptions,
    IReadOnlyDictionary<string, string> Groups);

/// <summary>
///     One command in the surface: its <see cref="RtCommandInfo" /> metadata projected
///     for serialization, plus the CLI options and arguments declared by its
///     <c>System.CommandLine.Command</c>.
/// </summary>
internal sealed record CommandDescriptor(
    string Name,
    string Category,
    bool Mutates,
    bool JsonOutput,
    string WhenToUse,
    string Description,
    IReadOnlyList<string> Examples,
    IReadOnlyList<OptionDescriptor> Options,
    IReadOnlyList<ArgumentDescriptor> Arguments,
    string? Group = null,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? SeeAlso = null,
    IReadOnlyList<string>? KnownIssues = null,
    IReadOnlyList<string>? SupportedFormats = null);

/// <summary>
///     A single CLI option (flag) — its canonical name, all aliases (with prefixes,
///     e.g. <c>--solution</c>, <c>-s</c>), whether it is required, its value type, and help text.
/// </summary>
internal sealed record OptionDescriptor(
    string Name,
    IReadOnlyList<string> Aliases,
    bool Required,
    string ValueType,
    string? Description = null);

/// <summary>
///     A single positional argument — its name, whether it must be supplied (arity minimum &gt; 0),
///     its value type, and help text.
/// </summary>
internal sealed record ArgumentDescriptor(
    string Name,
    bool Required,
    string ValueType,
    string? Description = null);
