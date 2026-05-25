using System.Reflection;

using RoslynTools.Core;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Locks the shape of the category and capability marker interfaces introduced in EP-013
///     Phase 1: <c>IInspectCommand</c>, <c>IAnalyzeCommand</c>, <c>IRefactorCommand</c>,
///     <c>IMutatingCommand</c>, <c>IBatchMutationCommand</c>. Together with the pre-existing
///     <c>IStructuredOutputCommand</c>, these markers drive App.cs static-ctor invariants and
///     downstream architecture tests added in later batches.
/// </summary>
public sealed class CategoryInterfacesShape
{
    private static readonly Assembly CoreAssembly = typeof(IRtCommand).Assembly;

    [Fact]
    public void with_each_new_marker_the_interface_exists_at_expected_fqn()
    {
        AssertInterfaceExists("RoslynTools.Core.IInspectCommand");
        AssertInterfaceExists("RoslynTools.Core.IAnalyzeCommand");
        AssertInterfaceExists("RoslynTools.Core.IRefactorCommand");
        AssertInterfaceExists("RoslynTools.Core.IMutatingCommand");
        AssertInterfaceExists("RoslynTools.Core.IBatchMutationCommand");
    }

    [Fact]
    public void with_each_new_marker_the_interface_is_internal()
    {
        AssertInterfaceIsInternal("RoslynTools.Core.IInspectCommand");
        AssertInterfaceIsInternal("RoslynTools.Core.IAnalyzeCommand");
        AssertInterfaceIsInternal("RoslynTools.Core.IRefactorCommand");
        AssertInterfaceIsInternal("RoslynTools.Core.IMutatingCommand");
        AssertInterfaceIsInternal("RoslynTools.Core.IBatchMutationCommand");
    }

    [Fact]
    public void with_each_new_marker_the_interface_declares_no_members()
    {
        AssertInterfaceIsEmpty("RoslynTools.Core.IInspectCommand");
        AssertInterfaceIsEmpty("RoslynTools.Core.IAnalyzeCommand");
        AssertInterfaceIsEmpty("RoslynTools.Core.IRefactorCommand");
        AssertInterfaceIsEmpty("RoslynTools.Core.IMutatingCommand");
        AssertInterfaceIsEmpty("RoslynTools.Core.IBatchMutationCommand");
    }

    [Fact]
    public void with_read_only_category_markers_the_direct_parent_is_irtcommand()
    {
        AssertDirectParents("RoslynTools.Core.IInspectCommand", "RoslynTools.Core.IRtCommand");
        AssertDirectParents("RoslynTools.Core.IAnalyzeCommand", "RoslynTools.Core.IRtCommand");
    }

    [Fact]
    public void with_imutatingcommand_the_direct_parent_is_irtcommand()
    {
        AssertDirectParents("RoslynTools.Core.IMutatingCommand", "RoslynTools.Core.IRtCommand");
    }

    [Fact]
    public void with_mutating_subtypes_the_direct_parent_is_imutatingcommand()
    {
        // Every refactor IS a mutation; every batch-mutation IS a mutation. Both inherit
        // IMutatingCommand so commands declare only the more specific marker and pick up
        // --dry-run / change-summary semantics transitively.
        AssertDirectParents("RoslynTools.Core.IRefactorCommand", "RoslynTools.Core.IMutatingCommand");
        AssertDirectParents("RoslynTools.Core.IBatchMutationCommand", "RoslynTools.Core.IMutatingCommand");
    }

    [Fact]
    public void with_istructuredoutputcommand_the_existing_shape_is_unchanged()
    {
        // Regression guard: this batch must not modify IStructuredOutputCommand, and the empty-marker
        // assertion is also tested here against a known-good empty interface (proves the BindingFlags
        // combination actually returns zero members on an interface that has none).
        var t = CoreAssembly.GetType("RoslynTools.Core.IStructuredOutputCommand", throwOnError: false);
        Assert.NotNull(t);
        Assert.True(t!.IsInterface, "RoslynTools.Core.IStructuredOutputCommand must be an interface.");
        Assert.False(t.IsVisible, "RoslynTools.Core.IStructuredOutputCommand must remain internal.");

        var members = t.GetMembers(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.True(
            members.Length == 0,
            $"IStructuredOutputCommand must remain an empty marker. Found {members.Length} declared member(s): "
            + string.Join(", ", members.Select(m => m.Name)));
    }

    private static void AssertInterfaceExists(string fqn)
    {
        var t = CoreAssembly.GetType(fqn, throwOnError: false);
        Assert.True(t is not null, $"Expected interface {fqn} to exist in {CoreAssembly.GetName().Name}.");
        Assert.True(t!.IsInterface, $"{fqn} must be declared as an interface.");
    }

    private static void AssertInterfaceIsInternal(string fqn)
    {
        var t = CoreAssembly.GetType(fqn, throwOnError: false);
        Assert.NotNull(t);
        // An internal type is non-public but visible to this test via InternalsVisibleTo.
        Assert.False(
            t!.IsVisible,
            $"{fqn} must be internal (not public). Marker interfaces are an internal architectural contract.");
    }

    private static void AssertInterfaceIsEmpty(string fqn)
    {
        var t = CoreAssembly.GetType(fqn, throwOnError: false);
        Assert.NotNull(t);
        var members = t!.GetMembers(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.True(
            members.Length == 0,
            $"{fqn} must declare no members (empty marker). Found {members.Length}: "
            + string.Join(", ", members.Select(m => m.Name)));
    }

    private static void AssertDirectParents(string fqn, params string[] expectedParentFqns)
    {
        var t = CoreAssembly.GetType(fqn, throwOnError: false);
        Assert.NotNull(t);

        var allInterfaces = t!.GetInterfaces();
        var transitivelyInherited = allInterfaces.SelectMany(i => i.GetInterfaces()).ToHashSet();
        var directParents = allInterfaces.Where(i => !transitivelyInherited.Contains(i)).ToArray();

        var actual = directParents.Select(i => i.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var expected = expectedParentFqns.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.True(
            actual.SequenceEqual(expected),
            $"{fqn} must directly inherit exactly [{string.Join(", ", expected)}]. "
            + $"Found direct parents: [{string.Join(", ", actual)}]. "
            + $"All inherited (transitive): [{string.Join(", ", allInterfaces.Select(i => i.FullName))}].");
    }
}
