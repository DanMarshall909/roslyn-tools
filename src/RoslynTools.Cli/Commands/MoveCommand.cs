using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Moves code elements within the solution.
/// </summary>
internal sealed class MoveCommand : IRtCommand
{
    private MoveCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "move",
        true,
        false,
        "refactor",
        Aliases: null,
        WhenToUse:
        "Moving a type to a different namespace — or batch-moving all types in a namespace — while updating the declaration and all reference sites (using directives and fully-qualified names). Use --move-file to also relocate the source file(s) to match the new namespace folder structure. Use --to-project to physically move a file across project boundaries while keeping its namespace intact.",
        Examples:
        [
            "rt refactor move --type ProjectX.Core.Services.BookingService --to-namespace ProjectX.Domain.Services --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor move --namespace ProjectX.Core.Models --to-namespace ProjectX.Domain.Entities --move-file --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor move --type RoslynTools.Commands.RenameCommand --to-namespace RoslynTools.Commands.Rename --solution RoslynTools.slnx",
            "rt refactor move --type MyApp.Api.Models.Product --to-project MyApp.Domain --solution MyApp.sln",
            "rt refactor move --type MyApp.Api.Models.Product --to-project MyApp.Domain --to-namespace MyApp.Domain.Models --solution MyApp.sln"
        ]
    );

    public static Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var typeOpt = new Option<string?>("--type",
            "Fully-qualified type name (e.g. ProjectX.Core.Services.BookingService)");
        var nsFromOpt = new Option<string?>("--namespace",
            "Source namespace — moves ALL types in this namespace (batch mode)");
        var nsToOpt = new Option<string?>("--to-namespace",
            "Target namespace (e.g. ProjectX.Core.Domain.Services). Optional when --to-project is provided.");
        var toProjectOpt = new Option<string?>("--to-project",
            "Target project name — moves the file to that project's folder while keeping its namespace. " +
            "When combined with --to-namespace, also rewrites the namespace and updates consumers.");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var moveFileOpt = new Option<bool>("--move-file",
            "Move source file(s) to match the target namespace folder structure");

        var cmd = new Command("move",
            "Move a type to a different namespace, updating the declaration and all references")
        {
            solutionOpt, typeOpt, nsFromOpt, nsToOpt, toProjectOpt, dryRunOpt, moveFileOpt
        };

        cmd.SetHandler(async (solution, type, nsFrom, nsTo, toProject, dryRun, moveFile) =>
            {
                if (type == null && nsFrom == null)
                {
                    RtLog.Error("Error: either --type or --namespace is required.");
                    return;
                }

                if (type != null && nsFrom != null)
                {
                    RtLog.Error("Error: --type and --namespace are mutually exclusive.");
                    return;
                }

                if (toProject == null && nsTo == null)
                {
                    RtLog.Error("Error: either --to-namespace or --to-project is required.");
                    return;
                }

                var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
                using (workspace)
                {
                    if (nsFrom != null)
                        await RtServiceProvider.Get<MoveService>()
                            .ExecuteNamespaceMoveAsync(workspace, sln, nsFrom, nsTo!, moveFile, dryRun);
                    else if (toProject != null)
                        await RtServiceProvider.Get<MoveService>()
                            .ExecuteCrossProjectMoveAsync(workspace, sln, type!, toProject, nsTo, dryRun);
                    else
                        await RtServiceProvider.Get<MoveService>()
                            .ExecuteSingleTypeMoveAsync(workspace, sln, type!, nsTo!, moveFile, dryRun);
                }
            },
            solutionOpt, typeOpt, nsFromOpt, nsToOpt, toProjectOpt, dryRunOpt, moveFileOpt);

        return cmd;
    }
}
