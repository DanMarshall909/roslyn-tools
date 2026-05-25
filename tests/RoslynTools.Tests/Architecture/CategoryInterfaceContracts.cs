using System.CommandLine;
using System.Reflection;

using RoslynTools.Core;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Locks the category↔Info biconditionals introduced in EP-013 Phase 1 Batch 4 (US-107).
///     For every concrete <c>*Command</c> in <c>RoslynTools.Commands</c> (excluding
///     <c>DumpHelpCommand</c>):
///     <list type="bullet">
///         <item><see cref="IInspectCommand" /> ⇒ Category="inspect", Mutates=false, JsonOutput=true.</item>
///         <item><see cref="IAnalyzeCommand" /> ⇒ Category="analyze", Mutates=false, JsonOutput=true.</item>
///         <item><see cref="IRefactorCommand" /> ⇒ Category="refactor", Mutates=true, and <c>Build()</c>
///         registers a <c>--dry-run</c> option.</item>
///     </list>
///     The Category half is also asserted by <see cref="CategoryInterfaceCoverage" />; the
///     duplication is deliberate so this file owns the full triple-fact contract per marker
///     and a single failure tells you exactly which fact drifted.
/// </summary>
public sealed class CategoryInterfaceContracts
{
    private static readonly Assembly CliAssembly = typeof(App).Assembly;
    private const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;

    private static IEnumerable<Type> AllCommandTypes() =>
        CliAssembly.GetTypes()
            .Where(t => t.Namespace == "RoslynTools.Commands"
                        && !t.IsInterface
                        && t.Name.EndsWith("Command", StringComparison.Ordinal)
                        && t.Name != "DumpHelpCommand"
                        && typeof(IRtCommand).IsAssignableFrom(t));

    private static RtCommandInfo? InfoOf(Type t) =>
        (RtCommandInfo?)t.GetProperty("Info", StaticPublic)?.GetValue(null);

    [Fact]
    public void with_every_iinspectcommand_the_info_matches_category_inspect_readonly_json()
    {
        var offenders = AllCommandTypes()
            .Where(t => typeof(IInspectCommand).IsAssignableFrom(t))
            .Select(t => (Name: t.Name, Info: InfoOf(t)))
            .Where(x => x.Info is null
                        || x.Info.Category != "inspect"
                        || x.Info.Mutates
                        || !x.Info.JsonOutput)
            .Select(x => $"{x.Name} (Category='{x.Info?.Category}', Mutates={x.Info?.Mutates}, JsonOutput={x.Info?.JsonOutput})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every IInspectCommand must have Info.Category=='inspect', Info.Mutates==false, Info.JsonOutput==true. "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void with_every_ianalyzecommand_the_info_matches_category_analyze_readonly_json()
    {
        var offenders = AllCommandTypes()
            .Where(t => typeof(IAnalyzeCommand).IsAssignableFrom(t))
            .Select(t => (Name: t.Name, Info: InfoOf(t)))
            .Where(x => x.Info is null
                        || x.Info.Category != "analyze"
                        || x.Info.Mutates
                        || !x.Info.JsonOutput)
            .Select(x => $"{x.Name} (Category='{x.Info?.Category}', Mutates={x.Info?.Mutates}, JsonOutput={x.Info?.JsonOutput})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every IAnalyzeCommand must have Info.Category=='analyze', Info.Mutates==false, Info.JsonOutput==true. "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void with_every_irefactorcommand_the_info_matches_category_refactor_and_mutates()
    {
        var offenders = AllCommandTypes()
            .Where(t => typeof(IRefactorCommand).IsAssignableFrom(t))
            .Select(t => (Name: t.Name, Info: InfoOf(t)))
            .Where(x => x.Info is null
                        || x.Info.Category != "refactor"
                        || !x.Info.Mutates)
            .Select(x => $"{x.Name} (Category='{x.Info?.Category}', Mutates={x.Info?.Mutates})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every IRefactorCommand must have Info.Category=='refactor' and Info.Mutates==true. "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void with_every_irefactorcommand_the_built_command_registers_a_dry_run_option()
    {
        // App.Commands already encapsulates static-vs-instance Build() dispatch and DI
        // resolution for ctor-injected commands. Using that registry here means the test
        // exercises the exact factory that the running CLI uses — no parallel reflection
        // logic to drift from production. Per memory feedback_test_di_resolution.md, this
        // is the canonical resolution path; we do NOT hand-construct command instances.
        var refactorBuilders = App.Commands
            .Where(c => string.Equals(c.Info.Category, "refactor", StringComparison.Ordinal))
            .ToList();

        Assert.True(refactorBuilders.Count > 0, "Expected at least one refactor command in App.Commands.");

        var offenders = new List<string>();
        foreach (var (build, info) in refactorBuilders)
        {
            Command cmd;
            try
            {
                cmd = build();
            }
            catch (Exception ex)
            {
                offenders.Add($"{info.Name} (Build() threw: {ex.GetType().Name}: {ex.Message})");
                continue;
            }

            var hasDryRun = cmd.Options.Any(o =>
                string.Equals(o.Name, "dry-run", StringComparison.Ordinal)
                || o.HasAlias("--dry-run"));

            if (!hasDryRun)
            {
                var optionList = string.Join(", ", cmd.Options.Select(o => o.Name));
                offenders.Add($"{info.Name} (options: [{optionList}])");
            }
        }

        offenders.Sort(StringComparer.Ordinal);

        Assert.True(
            offenders.Count == 0,
            "Every IRefactorCommand's Build() must register a --dry-run option (the refactor-category "
            + "contract — every mutation must be previewable). "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }
}
