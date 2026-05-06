using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands.CommandHandlers;

internal static class ChangeSignatureCommandHandlers
{
    internal static async Task<int> RenameParamAsync(
        string methodSpec,
        string oldName,
        string newName,
        string? solution,
        bool dryRun)
    {
        var (workspace, loadedSolution) = await WorkspaceLoader.LoadAsync(solution);
        using (workspace)
        {
            try
            {
                var updatedSolution = await RtServiceProvider.Get<ChangeSignatureService>().RenameParamAsync(
                    loadedSolution,
                    methodSpec,
                    oldName,
                    newName);

                await SolutionWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                return CommandHandlerConstants.SuccessExitCode;
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                return CommandHandlerConstants.ErrorExitCode;
            }
        }
    }

    internal static async Task<int> ReorderAsync(
        string methodSpec,
        string[] newOrder,
        string? solution,
        bool dryRun)
    {
        var (workspace, loadedSolution) = await WorkspaceLoader.LoadAsync(solution);
        using (workspace)
        {
            try
            {
                var updatedSolution = await RtServiceProvider.Get<ChangeSignatureService>().ReorderParamsAsync(
                    loadedSolution,
                    methodSpec,
                    newOrder);

                await SolutionWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                return CommandHandlerConstants.SuccessExitCode;
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                return CommandHandlerConstants.ErrorExitCode;
            }
        }
    }

    internal static async Task<int> AddParamAsync(
        string methodSpec,
        string paramSpec,
        string? after,
        string? solution,
        bool dryRun)
    {
        var (workspace, loadedSolution) = await WorkspaceLoader.LoadAsync(solution);
        using (workspace)
        {
            try
            {
                var updatedSolution = await RtServiceProvider.Get<ChangeSignatureService>().AddParamAsync(
                    loadedSolution,
                    methodSpec,
                    paramSpec,
                    after);

                await SolutionWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                return CommandHandlerConstants.SuccessExitCode;
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                return CommandHandlerConstants.ErrorExitCode;
            }
        }
    }

    internal static async Task<int> RemoveParamAsync(
        string methodSpec,
        string paramName,
        bool force,
        string? solution,
        bool dryRun)
    {
        var (workspace, loadedSolution) = await WorkspaceLoader.LoadAsync(solution);
        using (workspace)
        {
            try
            {
                var updatedSolution = await RtServiceProvider.Get<ChangeSignatureService>().RemoveParamAsync(
                    loadedSolution,
                    methodSpec,
                    paramName,
                    force);

                await SolutionWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                return CommandHandlerConstants.SuccessExitCode;
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                return CommandHandlerConstants.ErrorExitCode;
            }
        }
    }
}
