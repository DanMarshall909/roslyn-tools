using System.CommandLine;
using System.CommandLine.Help;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.CommandSurface;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

/// <summary>
///     Displays the full command reference by walking the registry in <see cref="App" />.
///     When stdout is a terminal: coloured, categorised output.
///     When stdout is redirected (e.g. explicit command-reference generation): Markdown suitable for <c>docs/COMMANDS.md</c>.
///     Listed in the root help's short discovery section while the full catalogue
///     remains limited to product commands.
/// </summary>
internal static class DumpHelpCommand
{
    /// <summary>
    /// Builds the help output for a command.
    /// </summary>
    internal static Command Build()
    {
        var cmd = new Command("dump-help",
            "Show the full command reference (coloured in terminal, Markdown when piped)");

        var compactOpt = new Option<bool>("--compact",
            "One-line-per-command catalog (<name> | <category> | <description>) — ~500 tokens, agent-friendly");
        cmd.AddOption(compactOpt);

        cmd.SetHandler(ctx =>
        {
            var compact = ctx.ParseResult.GetValueForOption(compactOpt);
            var snapshot = App.CatalogCommands
                .Select(e => (Cmd: e.Build(), e.Info))
                .ToList();
            var products = ResolveProducts(ctx.BindingContext.GetService<IServiceProvider>());

            if (compact)
            {
                RenderCompact(snapshot, products);
                return;
            }

            switch (RtContext.Format)
            {
                case OutputFormat.Json:
                case OutputFormat.Yaml:
                    WriteStructured(snapshot, products);
                    break;
                case OutputFormat.Markdown:
                    // Explicit --markdown is honoured even in a terminal (previously ignored).
                    RtResultRenderer.WriteText(RtResult<string>.Ok(RenderMarkdown(snapshot, products)));
                    break;
                default:
                    // Text: Markdown when piped for explicit docs generation, coloured in a terminal.
                    if (RtConsole.IsOutputRedirected)
                        RtResultRenderer.WriteText(RtResult<string>.Ok(RenderMarkdown(snapshot, products)));
                    else
                        RenderColoured(snapshot, products);
                    break;
            }
        });

        return cmd;
    }

    /// <summary>
    ///     One-line-per-command output: <c>&lt;name&gt; | &lt;category&gt; | &lt;description&gt;</c>.
    ///     ~500 tokens for the full catalog — agent-friendly alternative to the full dump.
    /// </summary>
    private static void RenderCompact(
        IReadOnlyList<(Command Cmd, RtCommandInfo Info)> snapshot,
        IReadOnlyDictionary<string, CommandProductMetadata>? products = null)
    {
        var sb = new StringBuilder();
        foreach (var (cmd, info) in snapshot.OrderBy(e => e.Info.Category).ThenBy(e => e.Info.Name))
        {
            var raw = cmd.Description;
            var desc = string.IsNullOrEmpty(raw) ? info.WhenToUse ?? "" : raw;
            var firstLine = desc.Split('\n', 2)[0].Trim();
            if (ResolveSafety(info, products).Effect == CommandEffect.LocalStateWrite)
                firstLine = $"[local-state-write] {firstLine}";
            if (firstLine.Length > 90) firstLine = firstLine[..89] + "…";
            var placement = IsRootOnly(info)
                ? $"{info.Category} (root)"
                : info.Category;
            sb.AppendLineF($"{info.Name,-28} | {placement,-16} | {firstLine}");
        }

        RtResultRenderer.WriteText(RtResult<string>.Ok(sb.ToString().TrimEnd()));
    }

    /// <summary>
    ///     Emits the full command surface — each command's metadata plus its options and
    ///     positional arguments, and the host's global options — as JSON or YAML, dispatching
    ///     on <see cref="RtContext.Format" />. Backs <c>rt dump-help --json</c> / <c>--yaml</c>;
    ///     the JSON output is described by <c>docs/command-surface.schema.json</c>.
    /// </summary>
    private static void WriteStructured(
        IReadOnlyList<(Command Cmd, RtCommandInfo Info)> snapshot,
        IReadOnlyDictionary<string, CommandProductMetadata>? products)
    {
        // Global options live on the root, not on individual commands, so reconstruct them
        // from the canonical definition rather than walking any single command.
        var (jsonOpt, llmOpt, markdownOpt, yamlOpt, decisionsOpt, relaxJsonOpt,
            showWorkspaceWarningsOpt, fullWorkspaceEvidenceOpt, noSuggestOpt, trustRepositoryOpt) = App.RegisterGlobalOptions(new RootCommand());
        var globalOptions = new Option[]
        {
            jsonOpt, llmOpt, markdownOpt, yamlOpt, relaxJsonOpt, decisionsOpt,
            showWorkspaceWarningsOpt, fullWorkspaceEvidenceOpt, noSuggestOpt, trustRepositoryOpt
        };

        var model = CommandSurfaceBuilder.Build(
            snapshot,
            App.GroupDescriptions,
            globalOptions,
            products);

        var text = RtContext.Format == OutputFormat.Yaml
            ? CommandSurfaceBuilder.ToYaml(model)
            : CommandSurfaceBuilder.ToJson(model);

        RtResultRenderer.WriteText(RtResult<string>.Ok(text));
    }

    /// <summary>
    /// Renders the help output as markdown.
    /// </summary>
    internal static string RenderMarkdown(
        IReadOnlyList<(Command Cmd, RtCommandInfo Info)> registry,
        IReadOnlyDictionary<string, CommandProductMetadata>? products = null)
    {
        var sb = new StringBuilder();

        var byCategory = GroupByPlacementAndCategory(registry);

        BuildHeader(sb, registry);
        BuildContents(sb, byCategory);
        BuildIndex(sb, registry);

        foreach (var group in byCategory) BuildGroupSection(sb, group, products);

        return sb.ToString();
    }

    /// <summary>
    /// Builds the header for the help output.
    /// </summary>
    internal static void BuildHeader(StringBuilder sb, IReadOnlyList<(Command Cmd, RtCommandInfo Info)> registry)
    {
        sb.AppendLine("# rt Commands");
        sb.AppendLine();
        sb.AppendLine(
            "Auto-generated on every `dotnet build` from each command's static `RtCommandInfo Info` property. **Do not hand-edit.**");
        sb.AppendLine();
        sb.AppendLineF($"Total commands: **{registry.Count}**");
        sb.AppendLine();
        sb.AppendLine(
            "Source commands are filed under `inspect`, `analyze`, `refactor`, or `ci`; lifecycle commands are root-only. Invoke them as shown:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("rt <group> <command> [args...]   # canonical form");
        sb.AppendLine("rt <command> [args...]           # root shortcut (all unambiguous names)");
        sb.AppendLine("```");
        sb.AppendLine();
    }

    /// <summary>
    /// Builds the content of the help output.
    /// </summary>
    internal static void BuildContents(StringBuilder sb,
        IEnumerable<IGrouping<string, (Command Cmd, RtCommandInfo Info)>> byCategory)
    {
        sb.AppendLine("## Contents");
        sb.AppendLine();
        foreach (var group in byCategory)
        {
            var desc = App.CategoryDescriptions.TryGetValue(group.Key, out var d) ? d : "";
            var heading = IsRootOnly(group)
                ? "`rt`"
                : $"`rt {group.Key}`";
            sb.AppendLineF($"- **{heading}** — {desc}");
            sb.AppendLineF(
                $"  - {string.Join(", ", group.OrderBy(e => e.Info.Name).Select(e => $"[`{e.Info.Name}`](#{Anchor(e.Info.Name)}){(e.Info.Aliases is { Length: > 0 } a ? $" ({string.Join("/", a)})" : "")}"))}");
        }

        sb.AppendLine();
    }

    internal static void BuildIndex(
        StringBuilder sb,
        IReadOnlyList<(Command Cmd, RtCommandInfo Info)> registry)
    {
        sb.AppendLine("## Index");
        sb.AppendLine();
        sb.AppendLine("| Command | Group | Aliases | Description |");
        sb.AppendLine("|---------|-------|---------|-------------|");

        foreach (var (c, info) in registry.OrderBy(e => e.Info.Name))
        {
            var aliases = info.Aliases is { Length: > 0 }
                ? string.Join(", ", info.Aliases.Select(a => $"`{a}`"))
                : "—";
            var placement = IsRootOnly(info)
                ? "root"
                : $"`{info.Category}`";
            sb.AppendLineF(
                $"| [`{info.Name}`](#{Anchor(info.Name)}) | {placement} | {aliases} | {c.Description} |");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Builds a group section in the help output.
    /// </summary>
    internal static void BuildGroupSection(
        StringBuilder sb,
        IGrouping<string, (Command Cmd, RtCommandInfo Info)> group,
        IReadOnlyDictionary<string, CommandProductMetadata>? products = null)
    {
        var desc = App.CategoryDescriptions.TryGetValue(group.Key, out var d) ? d : "";
        var heading = IsRootOnly(group)
            ? "`rt`"
            : $"`rt {group.Key}`";
        sb.AppendLineF($"## {heading} — {desc}");
        sb.AppendLine();

        foreach (var (c, info) in group.OrderBy(e => e.Info.Name))
            BuildCommandSection(sb, c, info, products?.GetValueOrDefault(info.Name));
    }

    /// <summary>
    /// Builds a command section in the help output.
    /// </summary>
    internal static void BuildCommandSection(
        StringBuilder sb,
        Command c,
        RtCommandInfo info,
        CommandProductMetadata? product = null)
    {
        var shortDescription = c.Description ?? "(no description)";
        sb.AppendLineF($"### {info.Name}");
        sb.AppendLine();
        sb.AppendLineF($"_{shortDescription}_");
        sb.AppendLine();

        var badges = new List<string>
        {
            SafetyBadge(CommandSafety.ForPublication(info, product)),
            info.JsonOutput ? "JSON output" : "Plain-text output"
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
            foreach (var ex in info.Examples) sb.AppendLine(ex);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (info.KnownIssues is { Length: > 0 })
        {
            sb.AppendLine("**Known issues:**");
            sb.AppendLine();
            foreach (var issue in info.KnownIssues) sb.AppendLineF($"- {issue}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>
    /// Renders the help output with colored text.
    /// </summary>
    internal static void RenderColoured(
        IReadOnlyList<(Command Cmd, RtCommandInfo Info)> registry,
        IReadOnlyDictionary<string, CommandProductMetadata>? products = null)
    {
        var byCategory = GroupByPlacementAndCategory(registry).ToList();

        RtConsole.WriteLine(RtOutputContext.Ui, $"rt Commands  ({registry.Count} total)", ConsoleColor.White);
        RtConsole.WriteLine(RtOutputContext.Ui);

        foreach (var group in byCategory)
        {
            var desc = App.CategoryDescriptions.TryGetValue(group.Key, out var d) ? d : "";
            var heading = IsRootOnly(group) ? "root lifecycle" : $"rt {group.Key}";
            RtConsole.Write(RtOutputContext.Ui, $"  {heading}", CategoryColour(group.Key));
            RtConsole.WriteLine(RtOutputContext.Ui, $"   {desc}", ConsoleColor.DarkGray);
            RtConsole.WriteLine(RtOutputContext.Ui, "  " + new string('\u2500', 52), ConsoleColor.DarkGray);

            foreach (var (c, info) in group.OrderBy(e => e.Info.Name))
            {
                RtConsole.Write(RtOutputContext.Ui, $"  {info.Name}", ConsoleColor.White);

                if (info.Aliases is { Length: > 0 })
                    RtConsole.Write(RtOutputContext.Ui, $"  ({string.Join(", ", info.Aliases)})",
                        ConsoleColor.DarkGray);

                WriteSafety(ResolveSafety(info, products));

                RtConsole.WriteLine(RtOutputContext.Ui);
                RtConsole.WriteLine(RtOutputContext.Ui, $"    {c.Description}", ConsoleColor.Gray);
            }

            RtConsole.WriteLine(RtOutputContext.Ui);
        }
    }

    /// <summary>
    /// Renders a group of commands with colored text.
    /// </summary>
    internal static void RenderColouredGroup(
        string category,
        IReadOnlyList<(Command Cmd, RtCommandInfo Info)> snapshot,
        IReadOnlyDictionary<string, CommandProductMetadata>? products = null)
    {
        var desc = App.GroupDescriptions.TryGetValue(category, out var d) ? d : "";
        RtConsole.WriteLine(RtOutputContext.Ui);
        RtConsole.Write(RtOutputContext.Ui, $"  rt {category}", CategoryColour(category));
        RtConsole.WriteLine(RtOutputContext.Ui, $"   — {desc}", ConsoleColor.DarkGray);
        RtConsole.WriteLine(RtOutputContext.Ui, "  " + new string('─', 52), ConsoleColor.DarkGray);
        RtConsole.WriteLine(RtOutputContext.Ui);

        foreach (var (c, info) in snapshot.OrderBy(e => e.Info.Name))
        {
            RtConsole.Write(RtOutputContext.Ui, $"    {info.Name}", ConsoleColor.White);
            if (info.Aliases is { Length: > 0 })
                RtConsole.Write(RtOutputContext.Ui, $"  ({string.Join(", ", info.Aliases)})", ConsoleColor.DarkGray);
            WriteSafety(ResolveSafety(info, products));
            RtConsole.WriteLine(RtOutputContext.Ui);
            RtConsole.WriteLine(RtOutputContext.Ui, $"      {c.Description}", ConsoleColor.Gray);
        }

        RtConsole.WriteLine(RtOutputContext.Ui);
    }

    /// <summary>
    /// Renders the command header with colored text. Shim that forwards to the
    /// <see cref="RenderColouredCommandHeader(RtCommandInfo, string)"/> overload —
    /// the latter is the unit-testable surface, free of <see cref="HelpContext"/>.
    /// </summary>
    internal static void RenderColouredCommandHeader(
        HelpContext ctx,
        RtCommandInfo info,
        CommandProductMetadata? product = null)
        => RenderColouredCommandHeader(info, ctx.Command.Description ?? string.Empty, product);

    /// <summary>
    /// Renders the same rich content that <c>rt dump-help</c> emits for a single
    /// command: name + aliases + read-only/writes flag, description, When-to-use,
    /// Examples, supported output formats, and known-issues block. Drives the
    /// <c>rt &lt;cmd&gt; --help</c> output via <see cref="App.BuildHelpLayout"/>.
    /// </summary>
    internal static void RenderColouredCommandHeader(
        RtCommandInfo info,
        string description,
        CommandProductMetadata? product = null)
    {
        RtConsole.WriteLine(RtOutputContext.Ui);
        RtConsole.Write(RtOutputContext.Ui, $"  rt {info.Name}", ConsoleColor.White);
        if (info.Aliases is { Length: > 0 })
            RtConsole.Write(RtOutputContext.Ui, $"  ({string.Join(", ", info.Aliases)})", ConsoleColor.DarkGray);
        WriteSafety(CommandSafety.ForPublication(info, product));
        RtConsole.WriteLine(RtOutputContext.Ui);
        RtConsole.WriteLine(RtOutputContext.Ui, $"  {description}", ConsoleColor.Gray);
        RtConsole.WriteLine(RtOutputContext.Ui);
        RtConsole.WriteLine(RtOutputContext.Ui, "  When to use:", ConsoleColor.White);
        RtConsole.WriteLine(RtOutputContext.Ui, $"  {info.WhenToUse}", ConsoleColor.Gray);
        RtConsole.WriteLine(RtOutputContext.Ui);
        if (info.Examples is { Length: > 0 })
        {
            RtConsole.WriteLine(RtOutputContext.Ui, "  Examples:", ConsoleColor.White);
            foreach (var ex in info.Examples)
                RtConsole.WriteLine(RtOutputContext.Ui, $"    {ex}", ConsoleColor.DarkCyan);
            RtConsole.WriteLine(RtOutputContext.Ui);
        }

        if (info.SupportedFormats is { Length: > 0 })
        {
            RtConsole.Write(RtOutputContext.Ui, "  Output: ", ConsoleColor.White);
            RtConsole.WriteLine(RtOutputContext.Ui,
                string.Join(", ", info.SupportedFormats.Select(f => f.ToString())),
                ConsoleColor.Gray);
            RtConsole.WriteLine(RtOutputContext.Ui);
        }

        if (info.KnownIssues is { Length: > 0 })
        {
            RtConsole.WriteLine(RtOutputContext.Ui, "  Known issues:", ConsoleColor.White);
            foreach (var issue in info.KnownIssues)
                RtConsole.WriteLine(RtOutputContext.Ui, $"    - {issue}", ConsoleColor.DarkYellow);
            RtConsole.WriteLine(RtOutputContext.Ui);
        }

        RtConsole.WriteLine(RtOutputContext.Ui, "  " + new string('─', 52), ConsoleColor.DarkGray);
    }

    private static ConsoleColor CategoryColour(string category)
    {
        return category switch
        {
            "inspect" => ConsoleColor.Cyan,
            "analyze" => ConsoleColor.Yellow,
            "refactor" => ConsoleColor.Magenta,
            _ => ConsoleColor.White
        };
    }

    private static int CategoryOrder(string category)
    {
        return category switch
        {
            "inspect" => 0,
            "analyze" => 1,
            "refactor" => 2,
            _ => 3
        };
    }

    private static string Anchor(string name)
    {
        return name.ToLowerInvariant().Replace(' ', '-');
    }

    private static Dictionary<string, CommandProductMetadata> ResolveProducts(
        IServiceProvider? services) =>
        services?.GetServices<ICommandApplicationPort>()
            .Where(port => port is not ILegacyCommandApplicationPort)
            .GroupBy(port => port.Definition.Info.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Definition.Product,
                StringComparer.Ordinal)
        ?? new Dictionary<string, CommandProductMetadata>(StringComparer.Ordinal);

    private static CommandSafety ResolveSafety(
        RtCommandInfo info,
        IReadOnlyDictionary<string, CommandProductMetadata>? products = null) =>
        CommandSafety.ForPublication(info, products?.GetValueOrDefault(info.Name));

    private static string SafetyBadge(CommandSafety safety) => safety.Effect switch
    {
        CommandEffect.Mutation => "**Mutation** (writes repository files)",
        CommandEffect.LocalStateWrite => "**Local-state write** (does not write repository files)",
        _ => "**Read-only**"
    };

    private static string SafetyLabel(CommandSafety safety) => safety.Effect switch
    {
        CommandEffect.Mutation => "writes repository",
        CommandEffect.LocalStateWrite => "local-state-write",
        _ => "read-only"
    };

    private static void WriteSafety(CommandSafety safety)
    {
        var color = safety.Effect switch
        {
            CommandEffect.Mutation => ConsoleColor.DarkRed,
            CommandEffect.LocalStateWrite => ConsoleColor.DarkYellow,
            _ => ConsoleColor.DarkGreen
        };
        RtConsole.Write(RtOutputContext.Ui, $"  {SafetyLabel(safety)}", color);
    }

    private static bool IsRootOnly(
        IEnumerable<(Command Cmd, RtCommandInfo Info)> commands) =>
        commands.All(command => IsRootOnly(command.Info));

    private static bool IsRootOnly(RtCommandInfo info) =>
        info.Placement == CommandPlacement.RootOnly;

    private static IEnumerable<IGrouping<string, (Command Cmd, RtCommandInfo Info)>>
        GroupByPlacementAndCategory(
            IReadOnlyList<(Command Cmd, RtCommandInfo Info)> registry) =>
        registry
            .Where(entry => !IsRootOnly(entry.Info))
            .GroupBy(entry => entry.Info.Category)
            .Concat(registry
                .Where(entry => IsRootOnly(entry.Info))
                .GroupBy(entry => entry.Info.Category))
            .OrderBy(group => CategoryOrder(group.Key));
}
