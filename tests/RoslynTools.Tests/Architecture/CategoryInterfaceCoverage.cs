using System.Reflection;

using RoslynTools.Core;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Enforces the biconditional between every concrete <c>*Command</c> class in
///     <c>RoslynTools.Commands</c> and its category markers: each command
///     (excluding the <c>DumpHelp</c> meta-command) must
///     implement EXACTLY ONE of <see cref="IInspectCommand" />, <see cref="IAnalyzeCommand" />,
///     <see cref="ICiCommand" />, <see cref="IRefactorCommand" />, or
///     <see cref="IControlPlaneCommand" />. Mirrors the startup-time check in
///     <c>App.cs</c> static ctor — the test runs in CI, the static ctor runs at every CLI
///     invocation and during explicit <c>docs/COMMANDS.md</c> generation.
/// </summary>
public sealed class CategoryInterfaceCoverage
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
    public void with_every_command_at_least_one_category_marker_is_present()
    {
        var offenders = AllCommandTypes()
            .Where(t => !typeof(IInspectCommand).IsAssignableFrom(t)
                        && !typeof(IAnalyzeCommand).IsAssignableFrom(t)
                        && !typeof(ICiCommand).IsAssignableFrom(t)
                        && !typeof(IRefactorCommand).IsAssignableFrom(t)
                        && !typeof(IControlPlaneCommand).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every concrete *Command class in RoslynTools.Commands (excluding DumpHelpCommand) must "
            + "implement exactly one category marker. "
            + $"Missing a category marker ({offenders.Count}): "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void with_every_command_at_most_one_category_marker_is_present()
    {
        var offenders = AllCommandTypes()
            .Select(t => new
            {
                Name = t.Name,
                Markers = new[]
                {
                    typeof(IInspectCommand).IsAssignableFrom(t) ? "IInspectCommand" : null,
                    typeof(IAnalyzeCommand).IsAssignableFrom(t) ? "IAnalyzeCommand" : null,
                    typeof(ICiCommand).IsAssignableFrom(t) ? "ICiCommand" : null,
                    typeof(IRefactorCommand).IsAssignableFrom(t) ? "IRefactorCommand" : null,
                    typeof(IControlPlaneCommand).IsAssignableFrom(t) ? "IControlPlaneCommand" : null,
                }.Where(m => m is not null).ToArray()
            })
            .Where(x => x.Markers.Length > 1)
            .Select(x => $"{x.Name} [{string.Join(", ", x.Markers!)}]")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A command must declare exactly one category marker — no command may implement more than one of "
            + "IInspectCommand, IAnalyzeCommand, ICiCommand, IRefactorCommand, IControlPlaneCommand. Multiple markers found on: "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void with_each_category_marker_the_implementing_commands_info_category_matches()
    {
        const BindingFlags staticPublic = BindingFlags.Static | BindingFlags.Public;

        var mismatches = AllCommandTypes()
            .Select(t =>
            {
                var info = (RtCommandInfo?)t.GetProperty("Info", staticPublic)?.GetValue(null);
                string? expectedFromMarker =
                    typeof(IInspectCommand).IsAssignableFrom(t) ? "inspect"
                    : typeof(IAnalyzeCommand).IsAssignableFrom(t) ? "analyze"
                    : typeof(ICiCommand).IsAssignableFrom(t) ? "ci"
                    : typeof(IRefactorCommand).IsAssignableFrom(t) ? "refactor"
                    : typeof(IControlPlaneCommand).IsAssignableFrom(t) ? "control"
                    : null;
                return (Name: t.Name, ExpectedFromMarker: expectedFromMarker, ActualCategory: info?.Category);
            })
            .Where(x => x.ExpectedFromMarker is not null && x.ActualCategory != x.ExpectedFromMarker)
            .Select(x => $"{x.Name} (marker says '{x.ExpectedFromMarker}', Info.Category='{x.ActualCategory}')")
            .ToList();

        Assert.True(
            mismatches.Count == 0,
            "A command's category marker interface must agree with its RtCommandInfo.Category. Mismatches: "
            + string.Join("; ", mismatches));
    }
}
