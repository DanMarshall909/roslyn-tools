using System.CommandLine;
using System.Reflection;
using System.Text.RegularExpressions;

using RoslynTools.Commands;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Ratchets the US-114 flag-capability migration toward 100% command coverage.
/// </summary>
public sealed class FlagCapabilityMigrationTests
{
    private static readonly IReadOnlyDictionary<string, Type> OptionToCapability = RtFlagCapabilityMap.OptionToCapability;
    private static readonly IReadOnlyDictionary<Type, string[]> CapabilityToOptions = RtFlagCapabilityMap.CapabilityToOptions;

    [Fact]
    public void every_catalog_option_has_a_matching_capability_interface()
    {
        var offenders = RtFlagCatalog.CatalogOptionNames
            .Where(option => !OptionToCapability.ContainsKey(option))
            .OrderBy(option => option, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every catalog-owned option must have a matching ISupports* capability interface. "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void every_capability_interface_points_at_catalog_options()
    {
        var offenders = CapabilityToOptions
            .SelectMany(kv => kv.Value.Select(option => (Capability: kv.Key.Name, Option: option)))
            .Where(x => !RtFlagCatalog.IsCatalogOptionName(x.Option))
            .Select(x => $"{x.Capability} -> --{x.Option}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every ISupports* capability must point at catalog-owned options. "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void every_command_catalog_option_is_declared_in_the_capability_map()
    {
        var offenders = new List<string>();

        foreach (var entry in App.AllCommands)
        {
            Command cmd;
            try
            {
                cmd = entry.Build();
            }
            catch
            {
                continue;
            }

            foreach (var option in cmd.Options.Where(o => RtFlagCatalog.IsCatalogOptionName(o.Name)))
            {
                if (!OptionToCapability.ContainsKey(option.Name))
                    offenders.Add($"{entry.Info.Name}: --{option.Name} is catalog-owned but has no capability mapping");
            }
        }

        offenders.Sort(StringComparer.Ordinal);

        Assert.True(
            offenders.Count == 0,
            "Every built command option that belongs to RtFlagCatalog must have a capability mapping. "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void every_command_with_capability_exposes_matching_catalog_options()
    {
        var offenders = new List<string>();

        foreach (var entry in App.AllCommands)
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
                .ToHashSet(StringComparer.Ordinal);

            foreach (var capability in GetImplementedCapabilities(commandType))
            {
                foreach (var optionName in CapabilityToOptions[capability])
                {
                    if (!declaredOptionNames.Contains(optionName))
                        offenders.Add($"{entry.Info.Name}: implements {capability.Name} but built Command lacks --{optionName}");
                }
            }
        }

        offenders.Sort(StringComparer.Ordinal);

        Assert.True(
            offenders.Count == 0,
            "Every command/request type that implements an ISupports* capability must expose the matching catalog option(s). "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void migrated_command_files_do_not_parse_catalog_flags_ad_hoc()
    {
        var migratedCommandTypes = App.AllCommands
            .Select(c => ResolveCommandType(c.Info.Name))
            .Where(t => t is not null && GetImplementedCapabilities(t).Count > 0)
            .ToList();

        var sourceRoot = LocateRepoRoot();
        var offenders = new List<string>();

        foreach (var commandType in migratedCommandTypes)
        {
            var file = Path.Combine(sourceRoot, "src", "RoslynTools.Cli", "Commands", commandType!.Name + ".cs");
            if (!File.Exists(file))
                continue;

            var text = File.ReadAllText(file);
            var implementedFlags = GetImplementedCapabilities(commandType)
                .SelectMany(c => CapabilityToOptions[c])
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

    [Fact]
    public void every_mutation_command_is_review_first_without_an_explicit_apply_choice()
    {
        var offenders = new List<string>();

        foreach (var entry in App.AllCommands
                     .Where(entry => entry.Info.Mutates)
                     .Where(entry => !App.ControlPlaneCommandNames.Contains(entry.Info.Name)))
        {
            var command = entry.Build();
            var root = new RootCommand();
            root.AddCommand(command);

            foreach (var leaf in EnumerateLeaves(command, [], []))
            {
                var apply = leaf.Options.OfType<Option<bool>>()
                    .SingleOrDefault(option => option.Name == "apply");
                if (apply is not null)
                {
                    if (root.Parse(leaf.Path).GetValueForOption(apply))
                        offenders.Add($"{string.Join(' ', leaf.Path)}: --apply defaults to true");
                    continue;
                }

                var dryRun = leaf.Options.OfType<Option<bool>>()
                    .SingleOrDefault(option => option.Name == "dry-run");
                if (dryRun is null)
                {
                    offenders.Add($"{string.Join(' ', leaf.Path)}: no review/apply choice");
                    continue;
                }

                if (!root.Parse(leaf.Path).GetValueForOption(dryRun))
                    offenders.Add($"{string.Join(' ', leaf.Path)}: --dry-run defaults to false");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Every repository mutation leaf must default to review/dry-run and require an explicit apply choice; "
            + "IControlPlaneCommand uses its explicit local verb as the apply decision.\n"
            + string.Join("\n", offenders.OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static Type? ResolveCommandType(string commandName)
    {
        var className = ToCommandClassName(commandName);
        return typeof(App).Assembly.GetType($"RoslynTools.Commands.{className}", throwOnError: false);
    }

    private static IEnumerable<MutationLeaf> EnumerateLeaves(
        Command command,
        IReadOnlyList<string> parentPath,
        IReadOnlyList<Option> inheritedOptions)
    {
        var path = parentPath.Append(command.Name).ToArray();
        var options = inheritedOptions.Concat(command.Options).Distinct().ToArray();
        if (command.Subcommands.Count == 0)
        {
            yield return new MutationLeaf(path, options);
            yield break;
        }

        foreach (var child in command.Subcommands)
            foreach (var leaf in EnumerateLeaves(child, path, options))
                yield return leaf;
    }

    /// <summary>
    ///     Returns every <see cref="ISupportsSolutionOption" />-style capability interface implemented by the
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
            if (CapabilityToOptions.ContainsKey(i))
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

    private static string ToCamelCase(string value)
    {
        return char.ToLowerInvariant(value[0]) + value[1..];
    }

    private sealed record MutationLeaf(string[] Path, IReadOnlyList<Option> Options);

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
