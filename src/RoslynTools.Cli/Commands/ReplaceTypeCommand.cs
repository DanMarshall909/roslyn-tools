using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Replaces a type reference with another type.
/// </summary>
internal sealed class ReplaceTypeCommand : IRtCommand
{
    private ReplaceTypeCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "replace-datetime",
        true,
        false,
        "refactor",
        Aliases: null,
        WhenToUse:
        "Replacing all DateTime.UtcNow and DateTime.Now usages with injected TimeProvider calls (_timeProvider.GetUtcNow().UtcDateTime / GetLocalNow().DateTime) and automatically wiring TimeProvider into the affected class constructors. Run solution-wide before adding unit tests that need controllable time.",
        Examples:
        [
            "rt refactor replace-datetime --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor replace-datetime --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor replace-datetime --solution RoslynTools.slnx"
        ]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");

        var cmd = new Command("replace-datetime",
            "Replace DateTime.UtcNow / DateTime.Now with injected TimeProvider, adding constructor injection")
        {
            solutionOpt, dryRunOpt
        };

        cmd.SetHandler(async (solution, dryRun) =>
            {
                var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
                using (workspace)
                {
                    await RtServiceProvider.Get<ReplaceTypeService>().RunAsync(sln, dryRun);
                }
            },
            solutionOpt, dryRunOpt);

        return cmd;
    }
}
