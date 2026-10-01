using System.CommandLine;
using System.Reflection;

using RoslynTools.Core;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Locks the category↔Info biconditionals introduced in EP-013 Phase 1 Batch 4 (US-107).
///     For every concrete <c>*Command</c> in <c>RoslynTools.Commands</c> (excluding
///     <c>DumpHelpCommand</c>):
///     <list type="bullet">
///         <item><see cref="IInspectCommand" /> ⇒ Category="inspect", Mutates=false. (Hard ratchet.)</item>
///         <item><see cref="IAnalyzeCommand" /> ⇒ Category="analyze", Mutates=false. (Hard ratchet.)</item>
///         <item><see cref="IRefactorCommand" /> ⇒ Category="refactor", Mutates=true. (Hard ratchet.)</item>
///         <item><see cref="IControlPlaneCommand" /> ⇒ Category="control", root-only, Mutates=false.</item>
///         <item>Inspect/analyze ⇒ JsonOutput=true. <b>Soft (Skip)</b> — owned by US-082 (EP-007 umbrella).
///         Unskip when US-082 closes.</item>
///         <item><see cref="IRefactorCommand" />.Build() registers a <c>--dry-run</c> option, with
///         the documented known-offender exception <c>change-signature</c> tracked by US-112.</item>
///     </list>
///     The Category half is also asserted by <see cref="CategoryInterfaceCoverage" />; the
///     duplication is deliberate so this file owns the full per-marker contract and a single
///     failure tells you exactly which fact drifted.
/// </summary>
public sealed class CategoryInterfaceContracts
{
    [Fact]
    public void ci_commands_are_read_only_and_structured()
    {
        foreach (var command in App.Commands.Where(command => command.Info.Category == "ci"))
        {
            Assert.False(command.Info.Mutates);
            Assert.True(command.Info.JsonOutput);
        }
        Assert.Contains(App.Commands, command => command.Info.Category == "ci");
    }

    private static readonly Assembly CliAssembly = typeof(App).Assembly;
    private const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;

    /// <summary>
    ///     Documented known-offender exceptions to the IRefactorCommand ⇒ --dry-run rule.
    ///     Each entry MUST be tied to a backlog story that owns the fix; the test will fail
    ///     if any OTHER refactor command lacks --dry-run, ratcheting the contract while the
    ///     listed exceptions are being resolved.
    /// </summary>
    private static readonly HashSet<string> KnownDryRunExceptions = new(StringComparer.Ordinal)
    {
        "change-signature" // US-112: ChangeSignatureCommand.Build() registers no options at all
    };

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
    public void with_every_iinspectcommand_the_info_matches_category_inspect_and_readonly()
    {
        var offenders = AllCommandTypes()
            .Where(t => typeof(IInspectCommand).IsAssignableFrom(t))
            .Select(t => (Name: t.Name, Info: InfoOf(t)))
            .Where(x => x.Info is null
                        || x.Info.Category != "inspect"
                        || x.Info.Mutates)
            .Select(x => $"{x.Name} (Category='{x.Info?.Category}', Mutates={x.Info?.Mutates})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every IInspectCommand must have Info.Category=='inspect' and Info.Mutates==false. "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void with_every_ianalyzecommand_the_info_matches_category_analyze_and_readonly()
    {
        var offenders = AllCommandTypes()
            .Where(t => typeof(IAnalyzeCommand).IsAssignableFrom(t))
            .Select(t => (Name: t.Name, Info: InfoOf(t)))
            .Where(x => x.Info is null
                        || x.Info.Category != "analyze"
                        || x.Info.Mutates)
            .Select(x => $"{x.Name} (Category='{x.Info?.Category}', Mutates={x.Info?.Mutates})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every IAnalyzeCommand must have Info.Category=='analyze' and Info.Mutates==false. "
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
    public void with_every_control_plane_command_the_info_describes_root_local_control()
    {
        var controlCommands = AllCommandTypes()
            .Where(t => typeof(IControlPlaneCommand).IsAssignableFrom(t))
            .ToArray();
        Assert.NotEmpty(controlCommands);

        var offenders = controlCommands
            .Select(t => (Name: t.Name, Info: InfoOf(t)))
            .Where(x => x.Info is null
                        || x.Info.Category != "control"
                        || x.Info.Mutates
                        || x.Info.Placement != CommandPlacement.RootOnly)
            .Select(x => x.Name)
            .ToArray();
        Assert.Empty(offenders);
    }

    [Fact(Skip = "US-082 (EP-007 umbrella) owns adding structured JSON output to find-callers, find-refs, find-undocumented, find-unused, circular-deps, find-magic-strings, list-imports, list-smells. Unskip once US-082 closes — this test ratchets the category ⇒ JsonOutput biconditional.")]
    public void with_every_inspect_or_analyze_command_jsonoutput_is_true()
    {
        var offenders = AllCommandTypes()
            .Where(t => typeof(IInspectCommand).IsAssignableFrom(t)
                        || typeof(IAnalyzeCommand).IsAssignableFrom(t))
            .Select(t => (Name: t.Name, Info: InfoOf(t)))
            .Where(x => x.Info is null || !x.Info.JsonOutput)
            .Select(x => $"{x.Name} (JsonOutput={x.Info?.JsonOutput})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every IInspectCommand and IAnalyzeCommand must have Info.JsonOutput==true. "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }

    /// <summary>
    ///     US-100: Mirrors the biconditional enforced at startup in
    ///     <c>App.cs</c>'s static ctor — <c>IStructuredOutputCommand</c> ⇔ <c>Info.JsonOutput == true</c>.
    ///     Surfaces the invariant in the test suite so a violation lights up CI without booting the CLI.
    /// </summary>
    [Fact]
    public void with_every_command_istructuredoutputcommand_iff_jsonoutput_true()
    {
        var offenders = AllCommandTypes()
            .Select(t => (
                Name: t.Name,
                JsonOutput: InfoOf(t)?.JsonOutput ?? false,
                IsStructured: typeof(IStructuredOutputCommand).IsAssignableFrom(t)))
            .Where(x => x.JsonOutput != x.IsStructured)
            .Select(x => $"{x.Name} (JsonOutput={x.JsonOutput}, IStructuredOutputCommand={x.IsStructured})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every IRtCommand must satisfy: implements IStructuredOutputCommand ⇔ Info.JsonOutput==true. "
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
            .Where(c => !App.ControlPlaneCommandNames.Contains(c.Info.Name))
            .ToList();

        Assert.True(refactorBuilders.Count > 0, "Expected at least one refactor command in App.Commands.");

        var offenders = new List<string>();
        foreach (var (build, info) in refactorBuilders)
        {
            // Known-offender list — see KnownDryRunExceptions XML doc.
            if (KnownDryRunExceptions.Contains(info.Name))
                continue;

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
            "Every repository-source IRefactorCommand's Build() must register a --dry-run option. "
            + "IControlPlaneCommand is excluded because its explicit local verb is the apply decision. "
            + "Add new offenders to KnownDryRunExceptions "
            + "ONLY with a tracking story; the default is to fix the command. "
            + $"Offenders ({offenders.Count}): "
            + string.Join("; ", offenders));
    }
}
