using System.CommandLine;
using System.Text;
using RoslynTools.Core;

namespace RoslynTools.Commands;

/// <summary>
/// Emits <c>docs/COMMANDS.md</c> content to stdout by walking the command registry in <see cref="App"/>.
/// Does NOT call <c>MsBuildBootstrap.Register()</c> — pure in-process enumeration keeps the
/// post-build regeneration step fast. Hidden from the general <c>rt --help</c> listing; invoked
/// by the post-build MSBuild target.
/// </summary>
internal static class DumpHelpCommand
{
    internal static Command Build()
    {
        var cmd = new Command("dump-help", "Emit docs/COMMANDS.md content to stdout (auto-generated, do not invoke manually)")
        {
            IsHidden = true,
        };

        cmd.SetHandler(() =>
        {
            var md = RenderMarkdown(App.Commands);
            Console.Out.Write(md);
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
        sb.AppendLine($"Total commands: **{registry.Count}**");
        sb.AppendLine();

        var byCategory = registry
            .GroupBy(e => e.Info.Category)
            .OrderBy(g => CategoryOrder(g.Key));

        sb.AppendLine("## Contents");
        sb.AppendLine();
        foreach (var group in byCategory)
        {
            sb.AppendLine($"- **{group.Key}** — {string.Join(", ", group.OrderBy(e => e.Info.Name).Select(e => $"[`{e.Info.Name}`](#{Anchor(e.Info.Name)}){(e.Info.Aliases is { Length: > 0 } a ? $" ({string.Join("/", a)})" : "")}"))}");
        }
        sb.AppendLine();

        foreach (var group in byCategory)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();

            foreach (var (c, info) in group.OrderBy(e => e.Info.Name))
            {
                var shortDescription = c.Description ?? "(no description)";
                sb.AppendLine($"### {info.Name}");
                sb.AppendLine();
                sb.AppendLine($"_{shortDescription}_");
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
                    sb.AppendLine($"**Aliases:** {string.Join(", ", info.Aliases.Select(a => $"`{a}`"))}");
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
                        sb.AppendLine($"- {issue}");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

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
