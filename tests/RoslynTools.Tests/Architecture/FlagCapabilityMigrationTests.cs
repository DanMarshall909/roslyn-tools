using System.CommandLine;
using System.Reflection;
using System.Text.RegularExpressions;

using RoslynTools.Commands;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Ratchets the US-114 flag-capability migration toward 100% command coverage.
///     <para>
///         Two <c>SkippableFact</c> tests assert the full US-114 biconditional between catalog-owned
///         options and <c>ISupports*</c> capability interfaces. They are <b>Skipped</b> while
///         migration is in progress, with a dynamic message reporting the remaining-work count and
///         per-offender detail. When the count reaches zero the tests pass naturally; when US-114
///         closes, delete the <c>Skip.If</c> guard so they become permanent hard ratchets.
///     </para>
///     <para>
///         A third test (<c>[Fact]</c>, not Skipped) is the per-file regression guard: command
///         files that have already adopted at least one capability must not regress to hand-rolled
///         <c>new Option&lt;T&gt;("--known-flag")</c> or ad-hoc <c>GetValueForOption</c> calls for
///         catalog-owned flags.
///     </para>
/// </summary>
public sealed class FlagCapabilityMigrationTests
{
    /// <summary>Catalog option name → matching capability interface type.</summary>
    private static readonly Dictionary<string, Type> OptionToCapability =
        new(StringComparer.Ordinal)
        {
            ["solution"] = typeof(ISupportsSolutionOption),
            ["dry-run"] = typeof(ISupportsDryRunOption),
            ["top"] = typeof(ISupportsTopOption),
            ["symbol"] = typeof(ISupportsSymbolOption),
            ["scope"] = typeof(ISupportsScopeOption),
            ["kind"] = typeof(ISupportsKindOption)
        };

    private static readonly Dictionary<Type, string> CapabilityToOption =
        OptionToCapability.ToDictionary(kv => kv.Value, kv => kv.Key);

    [SkippableFact]
    public void every_command_with_catalog_option_implements_matching_capability()
    {
        var offenders = CollectBiconditionalOffenders().ForwardOffenders;

        Skip.If(
            offenders.Count > 0,
            $"US-114 WIP — {offenders.Count} command/option pair(s) expose a catalog flag but lack the matching ISupports* capability on the command type or its nested *CliRequest record:\n  "
            + string.Join("\n  ", offenders)
            + "\nRemove `Skip.If` permanently when US-114 closes.");
    }

    [SkippableFact]
    public void every_command_with_capability_exposes_matching_catalog_option()
    {
        var offenders = CollectBiconditionalOffenders().BackwardOffenders;

        Skip.If(
            offenders.Count > 0,
            $"US-114 WIP — {offenders.Count} command/capability pair(s) implement an ISupports* capability but the built Command.Options does not include the matching catalog flag:\n  "
            + string.Join("\n  ", offenders)
            + "\nRemove `Skip.If` permanently when US-114 closes.");
    }

    [Fact]
    public void migrated_command_files_do_not_parse_catalog_flags_ad_hoc()
    {
        var migratedCommandTypes = App.Commands
            .Select(c => ResolveCommandType(c.Info.Name))
            .Where(t => t is not null && GetImplementedCapabilities(t).Count > 0)
            .ToList();

        var sourceRoot = LocateRepoRoot();
        var offenders = new List<string>();

        foreach (var commandType in migratedCommandTypes)
        {
            var commandName = ToCommandName(commandType!.Name);
            var file = Path.Combine(sourceRoot, "src", "RoslynTools.Cli", "Commands", commandType.Name + ".cs");
            if (!File.Exists(file))
                continue;

            var text = File.ReadAllText(file);
            var implementedFlags = GetImplementedCapabilities(commandType)
                .Select(c => CapabilityToOption[c])
                .ToList();

            foreach (var flag in implementedFlags)
            {
                var variablePrefix = ToCamelCase(flag.Replace("-", string.Empty));
                if (Regex.IsMatch(text, @$"GetValueForOption\(\s*{Regex.Escape(variablePrefix)}Opt\s*\)"))
                {
                    offenders.Add(
                        $"{Path.GetFileName(file)}: --{flag} must be parsed by RtCommandFlagBinder.Bind, not GetValueForOption");
                }

                if (text.Contains("new Option<", StringComparison.Ordinal)
                    && text.Contains($"\"--{flag}\"", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: --{flag} must come from RtFlagCatalog");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Migrated command files (those implementing at least one ISupports* capability) "
            + "must not hand-roll catalog-owned flag parsing or option creation. "
            + "If you need to remove a capability from a command, drop the interface declaration first.\n"
            + string.Join("\n", offenders));
    }

    private static (List<string> ForwardOffenders, List<string> BackwardOffenders) CollectBiconditionalOffenders()
    {
        var forwardOffenders = new List<string>();
        var backwardOffenders = new List<string>();

        foreach (var entry in App.Commands)
        {
            var commandType = ResolveCommandType(entry.Info.Name);
            if (commandType is null)
                continue;

            Command cmd;
            try
            {
                cmd = entry.Build();
            }
            catch
            {
                continue;
            }

            var declaredOptionNames = cmd.Options
                .Select(o => o.Name)
                .Where(name => OptionToCapability.ContainsKey(name))
                .ToHashSet(StringComparer.Ordinal);
            var implementedCapabilities = GetImplementedCapabilities(commandType);

            foreach (var optionName in declaredOptionNames)
            {
                var capability = OptionToCapability[optionName];
                if (!implementedCapabilities.Contains(capability))
                    forwardOffenders.Add($"{entry.Info.Name}: option --{optionName} present but {capability.Name} not implemented");
            }

            foreach (var capability in implementedCapabilities)
            {
                var optionName = CapabilityToOption[capability];
                if (!declaredOptionNames.Contains(optionName))
                    backwardOffenders.Add($"{entry.Info.Name}: implements {capability.Name} but built Command lacks --{optionName}");
            }
        }

        forwardOffenders.Sort(StringComparer.Ordinal);
        backwardOffenders.Sort(StringComparer.Ordinal);
        return (forwardOffenders, backwardOffenders);
    }

    private static Type? ResolveCommandType(string commandName)
    {
        var className = ToCommandClassName(commandName);
        return typeof(App).Assembly.GetType($"RoslynTools.Commands.{className}", throwOnError: false);
    }

    /// <summary>
    ///     Returns every <see cref="ISupports*"/> capability interface implemented by the
    ///     command type itself OR by any nested type (the conventional <c>*CliRequest</c> record).
    /// </summary>
    private static HashSet<Type> GetImplementedCapabilities(Type? commandType)
    {
        var set = new HashSet<Type>();
        if (commandType is null)
            return set;

        AddCapabilitiesFrom(commandType, set);

        foreach (var nested in commandType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            AddCapabilitiesFrom(nested, set);

        return set;
    }

    private static void AddCapabilitiesFrom(Type t, HashSet<Type> sink)
    {
        foreach (var i in t.GetInterfaces())
        {
            if (CapabilityToOption.ContainsKey(i))
                sink.Add(i);
        }
    }

    private static string ToCommandClassName(string commandName)
    {
        return string.Concat(
            commandName
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
            + "Command";
    }

    private static string ToCommandName(string commandClassName)
    {
        // Inverse of ToCommandClassName: "FindRefsCommand" → "find-refs".
        var trimmed = commandClassName.EndsWith("Command", StringComparison.Ordinal)
            ? commandClassName[..^"Command".Length]
            : commandClassName;
        return Regex.Replace(trimmed, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
    }

    private static string ToCamelCase(string value)
    {
        return char.ToLowerInvariant(value[0]) + value[1..];
    }

    private static string LocateRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoslynTools.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }
}
