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
/// <remarks>
///     <para>
///         <c>Build()</c> is a convention enforced by the discoverer rather than an interface member,
///         because commands are migrating from a static <c>Build()</c> to an instance <c>Build()</c>
///         with constructor-injected services (US-072). The discoverer accepts either shape:
///     </para>
///     <list type="bullet">
///         <item><description>Static: <c>public static Command Build()</c> — original convention.</description></item>
///         <item><description>Instance: <c>public Command Build()</c> on a class with a DI-resolvable public constructor.</description></item>
///     </list>
///     <para>
///         Mixing the two is allowed during migration; once every command is instance-based, the
///         static path will be removed and <c>Build()</c> will become a true interface member.
///     </para>
/// </remarks>
internal interface IRtCommand
{
    /// <summary>
    ///     Rich metadata for the auto-generated <c>docs/COMMANDS.md</c> reference. Keep in sync
    ///     with the flags exposed by <c>Build()</c> — especially <c>Examples</c>.
    /// </summary>
    public static abstract RtCommandInfo Info { get; }
}
