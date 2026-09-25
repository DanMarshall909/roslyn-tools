using RoslynTools.Core.Output;

namespace RoslynTools.Core;

/// <summary>
///     Metadata describing a command for public discovery and the auto-generated
///     <c>docs/COMMANDS.md</c> reference. Grouped source commands expose this from their
///     <c>Commands/*Command.cs</c> type; typed application definitions can also supply it.
/// </summary>
internal sealed record RtCommandInfo(
    string Name,
    bool Mutates,
    bool JsonOutput,
    string Category,
    string WhenToUse,
    string[] Examples,
    string[]? KnownIssues = null,
    string[]? Aliases = null,
    string[]? SeeAlso = null,
    OutputFormat[]? SupportedFormats = null,
    string? Group = null,
    CommandPlacement Placement = CommandPlacement.CategoryAndRoot
);

internal enum CommandPlacement
{
    CategoryAndRoot,
    RootOnly
}
