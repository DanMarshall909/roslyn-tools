using System.CommandLine;
using System.Reflection;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools;

internal static class App
{
    /// <summary>
    /// Enforces the convention "every class in RoslynTools.Commands ending with 'Command'
    /// implements IRtCommand". Runs at first access to any <see cref="App"/> member, so it fires
    /// during startup — including the post-build <c>rt dump-help</c> target, which makes this a
    /// build-time check in practice.
    /// </summary>
    static App()
    {
        // Note: static classes in C# are marked abstract+sealed in IL, so we intentionally do
        // NOT filter by IsAbstract — a legacy static class that should implement IRtCommand needs
        // to be caught here so the dev converts it to `sealed class : IRtCommand`.
        var missing = typeof(App).Assembly.GetTypes()
            .Where(t => t.Namespace == "RoslynTools.Commands"
                        && !t.IsInterface
                        && t.Name.EndsWith("Command", StringComparison.Ordinal)
                        && t.Name != nameof(DumpHelpCommand)
                        && !typeof(IRtCommand).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "These classes in RoslynTools.Commands end with 'Command' but do not implement IRtCommand: "
                + string.Join(", ", missing)
                + ". Add `: IRtCommand` and supply static `Info` + `Build()`.");
        }
    }

    /// <summary>
    /// Registry of every <see cref="IRtCommand"/> in this assembly, with its <see cref="RtCommandInfo"/>.
    /// Discovered via reflection — add a new command by creating a class that implements
    /// <see cref="IRtCommand"/>; no edit to this file required.
    /// </summary>
    internal static IReadOnlyList<(Command Cmd, RtCommandInfo Info)> Commands { get; } = Discover();

    internal static async Task<int> RunAsync(string[] args)
    {
        var root = new RootCommand("Roslyn-based C# refactoring tool");

        foreach (var (cmd, _) in Commands)
        {
            root.AddCommand(cmd);
        }

        // Hidden meta-command, not part of the public registry (does not implement IRtCommand).
        root.AddCommand(DumpHelpCommand.Build());

        return await root.InvokeAsync(args);
    }

    private static IReadOnlyList<(Command, RtCommandInfo)> Discover()
    {
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public;

        var implementers = typeof(App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IRtCommand).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        var list = new List<(Command, RtCommandInfo)>(implementers.Length);
        foreach (var type in implementers)
        {
            var build = type.GetMethod("Build", staticFlags)
                        ?? throw new InvalidOperationException(
                            $"{type.Name} implements IRtCommand but exposes no public static Build() — "
                            + "check visibility (interface members are implicitly public).");
            var infoProp = type.GetProperty("Info", staticFlags)
                           ?? throw new InvalidOperationException(
                               $"{type.Name} implements IRtCommand but exposes no public static Info property.");

            var cmd = (Command)build.Invoke(null, null)!;
            var info = (RtCommandInfo)infoProp.GetValue(null)!;
            list.Add((cmd, info));
        }

        return list;
    }
}
