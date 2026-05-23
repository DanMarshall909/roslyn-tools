using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architecture invariants for the <see cref="RtCommandInfo.SeeAlso" />
///     field. Asserts every cross-link points at a real command, no command
///     references itself, and no list has duplicates. A failure here means a
///     command's <c>Info</c> was edited with a typo or stale reference.
/// </summary>
public sealed class SeeAlsoValidationTests
{
    private static readonly Dictionary<string, RtCommandInfo> Catalog =
        App.Commands.ToDictionary(e => e.Info.Name, e => e.Info, StringComparer.Ordinal);

    [Fact]
    public void every_see_also_entry_resolves_to_a_real_command()
    {
        var unknown = App.Commands
            .SelectMany(e => (e.Info.SeeAlso ?? []).Select(name => (Source: e.Info.Name, Target: name)))
            .Where(pair => !Catalog.ContainsKey(pair.Target))
            .Select(pair => $"{pair.Source} -> {pair.Target}")
            .ToList();

        unknown.ShouldBeEmpty(
            "SeeAlso entries must reference real command names. Unknown: " + string.Join(", ", unknown));
    }

    [Fact]
    public void no_command_lists_itself_in_see_also()
    {
        var selfRefs = App.Commands
            .Where(e => e.Info.SeeAlso?.Contains(e.Info.Name) == true)
            .Select(e => e.Info.Name)
            .ToList();

        selfRefs.ShouldBeEmpty(
            "Commands must not reference themselves in SeeAlso: " + string.Join(", ", selfRefs));
    }

    [Fact]
    public void see_also_lists_have_no_duplicates()
    {
        var dupes = App.Commands
            .Where(e => e.Info.SeeAlso is { Length: > 0 })
            .Where(e => e.Info.SeeAlso!.Length != e.Info.SeeAlso!.Distinct(StringComparer.Ordinal).Count())
            .Select(e => e.Info.Name)
            .ToList();

        dupes.ShouldBeEmpty(
            "Commands with duplicate entries in SeeAlso: " + string.Join(", ", dupes));
    }
}
