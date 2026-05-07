using System.CommandLine;

namespace RoslynTools.Core;

/// <summary>
///     Compile-time contract every command must satisfy. The reflection-based discovery in
///     <see cref="RoslynTools.App" /> enumerates all non-abstract types implementing this interface
///     and pulls <see cref="Info" /> plus a built <see cref="Command" /> from each.
///     New command? Drop a class in <c>Commands/</c>, implement this interface, done — the compiler
///     won't let you ship it without <see cref="Info" />, and the registry picks it up without any
///     hand-maintained list.
/// </summary>
internal interface IRtCommand
{
    /// <summary>
    ///     Rich metadata for the auto-generated <c>docs/COMMANDS.md</c> reference. Keep in sync
    ///     with the flags exposed by <see cref="Build" /> — especially <c>Examples</c>.
    /// </summary>
    public static abstract RtCommandInfo Info { get; }

    /// <summary>
    ///     Construct the System.CommandLine <see cref="Command" /> — options, arguments, handler.
    ///     Called once per process during registry discovery.
    /// </summary>
    public static abstract Command Build();
}
