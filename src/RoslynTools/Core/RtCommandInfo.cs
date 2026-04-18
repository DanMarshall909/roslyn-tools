namespace RoslynTools.Core;

/// <summary>
/// Metadata describing a command for the auto-generated <c>docs/COMMANDS.md</c> reference.
/// Every command in <c>Commands/*Command.cs</c> must expose a static <c>Info</c> property of this type.
/// </summary>
internal sealed record RtCommandInfo(
    string Name,
    bool Mutates,
    bool JsonOutput,
    string Category,
    string WhenToUse,
    string[] Examples,
    string[]? KnownIssues = null,
    string[]? Aliases = null
);
