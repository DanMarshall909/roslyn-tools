using System.Reflection;

using RoslynTools.Core;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Enforces the biconditional <c>IRefactorCommand ⇒ IMutatingCommand</c> introduced
///     in EP-013 Phase 1 Batch 3 (US-107). Every concrete <c>*Command</c> class in
///     <c>RoslynTools.Commands</c> that implements <see cref="IRefactorCommand" />
///     (the category marker for "this command produces file changes") must ALSO
///     implement <see cref="IMutatingCommand" /> (the capability marker for "produces
///     file changes; has <c>--dry-run</c>"). The two markers are intended to coincide
///     on the same set of types: <c>refactor == mutates</c>.
///
///     A second test method locks the implication <c>IBatchMutationCommand ⇒ IMutatingCommand</c>.
///     It tolerates the empty set: today no command implements <c>IBatchMutationCommand</c>,
///     so the assertion vacuously holds. The test still has value as a regression guard
///     against future churn — and although <c>IBatchMutationCommand</c> inherits from
///     <c>IMutatingCommand</c> at the type-system level (see <c>CategoryInterfacesShape</c>),
///     this test guarantees concrete adopters cannot accidentally bypass the marker via
///     hierarchy changes that survive compilation.
/// </summary>
public sealed class MutatingCommandCoverage
{
    private static readonly Assembly CliAssembly = typeof(App).Assembly;

    private static IEnumerable<Type> AllCommandTypes() =>
        CliAssembly.GetTypes()
            .Where(t => t.Namespace == "RoslynTools.Commands"
                        && !t.IsInterface
                        && t.Name.EndsWith("Command", StringComparison.Ordinal)
                        && t.Name != "DumpHelpCommand"
                        && typeof(IRtCommand).IsAssignableFrom(t));

    [Fact]
    public void with_every_refactor_command_the_imutatingcommand_marker_is_present()
    {
        var offenders = AllCommandTypes()
            .Where(t => typeof(IRefactorCommand).IsAssignableFrom(t)
                        && !typeof(IMutatingCommand).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every concrete *Command class implementing IRefactorCommand must also implement IMutatingCommand "
            + "(the capability marker for 'produces file changes; --dry-run required'). "
            + $"Missing IMutatingCommand ({offenders.Count}): "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void with_every_batch_mutation_command_the_imutatingcommand_marker_is_present()
    {
        var offenders = AllCommandTypes()
            .Where(t => typeof(IBatchMutationCommand).IsAssignableFrom(t)
                        && !typeof(IMutatingCommand).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every concrete *Command class implementing IBatchMutationCommand must also implement IMutatingCommand. "
            + $"Missing IMutatingCommand ({offenders.Count}): "
            + string.Join(", ", offenders));
    }
}
