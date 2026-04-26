using System.CommandLine;
using System.Text;
using RoslynTools.Core;

namespace RoslynTools.Commands;

/// <summary>
/// Displays the full command reference by walking the registry in <see cref="App"/>.
/// When stdout is a terminal: coloured, categorised output.
/// When stdout is redirected (e.g. the post-build MSBuild target): Markdown suitable for <c>docs/COMMANDS.md</c>.
/// Hidden from <c>rt --help</c> to keep the top-level list clean.
/// </summary>
internal static class DumpHelpCommand
{
    internal static Command Build()
    {
        var cmd = new Command("dump-help", "Show the full command reference (coloured in terminal, Markdown when piped)")
        {
            IsHidden = true,
        };

        cmd.SetHandler(() =>
        {
            if (Console.IsOutputRedirected)
                Console.Out.Write(RenderMarkdown(App.Commands));
            else
                RenderColoured(App.Commands);
        });

        return cmd;
    }

    internal static string RenderMarkdown(IReadOnlyList<(Command Cmd, RtCommandInfo Info)> registry)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# rt Commands");
        sb.AppendLine();
        sb.AppendLine("Auto-generated on every `dotnet build` from each command's static `RtCommandInfo Info` property. **Do not hand-edit.**");
        sb.AppendLine();
        sb.AppendLineF($"Total commands: **{registry.Count}**");
        sb.AppendLine();

        var byCategory = registry
            .GroupBy(e => e.Info.Category)
            .OrderBy(g => CategoryOrder(g.Key));

        sb.AppendLine("## Contents");
        sb.AppendLine();
        foreach (var group in byCategory)
        {
            sb.AppendLineF($"- **{group.Key}** — {string.Join(", ", group.OrderBy(e => e.Info.Name).Select(e => $"[`{e.Info.Name}`](#{Anchor(e.Info.Name)}){(e.Info.Aliases is { Length: > 0 } a ? $" ({string.Join("/", a)})" : "")}"))}");
        }
        sb.AppendLine();

        foreach (var group in byCategory)
        {
            sb.AppendLineF($"## {group.Key}");
            sb.AppendLine();

            foreach (var (c, info) in group.OrderBy(e => e.Info.Name))
            {
                var shortDescription = c.Description ?? "(no description)";
                sb.AppendLineF($"### {info.Name}");
                sb.AppendLine();
                sb.AppendLineF($"_{shortDescription}_");
                sb.AppendLine();

                var badges = new List<string>
                {
                    info.Mutates ? "**Mutation** (writes files)" : "**Read-only**",
                    info.JsonOutput ? "JSON output" : "Plain-text output",
                };
                sb.AppendLine(string.Join(" | ", badges));
                sb.AppendLine();

                if (info.Aliases is { Length: > 0 })
                {
                    sb.AppendLineF($"**Aliases:** {string.Join(", ", info.Aliases.Select(a => $"`{a}`"))}");
                    sb.AppendLine();
                }

                sb.AppendLine("**When to use:**");
                sb.AppendLine();
                sb.AppendLine(info.WhenToUse);
                sb.AppendLine();

                if (info.Examples.Length > 0)
                {
                    sb.AppendLine("**Examples:**");
                    sb.AppendLine();
                    sb.AppendLine("```bash");
                    foreach (var ex in info.Examples)
                    {
                        sb.AppendLine(ex);
                    }
                    sb.AppendLine("```");
                    sb.AppendLine();
                }

                if (info.KnownIssues is { Length: > 0 })
                {
                    sb.AppendLine("**Known issues:**");
                    sb.AppendLine();
                    foreach (var issue in info.KnownIssues)
                    {
                        sb.AppendLineF($"- {issue}");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    internal static void RenderColoured(IReadOnlyList<(Command Cmd, RtCommandInfo Info)> registry)
    {
        var byCategory = registry
            .GroupBy(e => e.Info.Category)
            .OrderBy(g => CategoryOrder(g.Key))
            .ToList();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"rt Commands  ({registry.Count} total)");
        Console.ResetColor();
        Console.WriteLine();

        foreach (var group in byCategory)
        {
            Console.ForegroundColor = CategoryColour(group.Key);
            Console.WriteLine($"  {group.Key.ToUpperInvariant()}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  " + new string('\u2500', 52));
            Console.ResetColor();

            foreach (var (c, info) in group.OrderBy(e => e.Info.Name))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  {info.Name}");

                if (info.Aliases is { Length: > 0 })
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"  ({string.Join(", ", info.Aliases)})");
                }

                Console.ForegroundColor = info.Mutates ? ConsoleColor.DarkRed : ConsoleColor.DarkGreen;
                Console.Write(info.Mutates ? "  writes" : "  read-only");

                Console.ResetColor();
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"    {c.Description}");
                Console.ResetColor();
            }

            Console.WriteLine();
        }
    }

    private static ConsoleColor CategoryColour(string category) => category switch
    {
        "Navigation" => ConsoleColor.Cyan,
        "Analysis" => ConsoleColor.Yellow,
        "Mutation" => ConsoleColor.Magenta,
        "Meta" => ConsoleColor.DarkGray,
        _ => ConsoleColor.White,
    };

    private static int CategoryOrder(string category) => category switch
    {
        "Navigation" => 0,
        "Analysis" => 1,
        "Mutation" => 2,
        "Meta" => 3,
        _ => 4,
    };

    private static string Anchor(string name) => name.ToLowerInvariant().Replace(' ', '-');
}
