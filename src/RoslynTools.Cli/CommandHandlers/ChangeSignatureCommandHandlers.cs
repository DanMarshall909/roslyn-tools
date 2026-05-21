using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands.CommandHandlers;

/// <summary>
/// Handles command signature changes for commands.
/// </summary>
internal static class ChangeSignatureCommandHandlers
{
    /// <summary>
    /// Renames a parameter in a command signature.
    /// </summary>
    internal static async Task<int> RenameParamAsync(
        IWorkspaceSessionFactory sessionFactory,
        ChangeSignatureService changeSignature,
        ISolutionChangeWriter solutionChangeWriter,
        string methodSpec,
        string oldName,
        string newName,
        string? solution,
        bool dryRun)
    {
        var (workspace, loadedSolution) = await sessionFactory.LoadWorkspaceAsync(solution);
        using (workspace)
        {
            try
            {
                var updatedSolution = await changeSignature.RenameParamAsync(
                    loadedSolution,
                    methodSpec,
                    oldName,
                    newName);

                await solutionChangeWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                return CommandHandlerConstants.SuccessExitCode;
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                return CommandHandlerConstants.ErrorExitCode;
            }
        }
    }

    /// <summary>
    /// Reorders parameters in a command signature.
    /// </summary>
    internal static async Task<int> ReorderAsync(
        IWorkspaceSessionFactory sessionFactory,
        ChangeSignatureService changeSignature,
        ISolutionChangeWriter solutionChangeWriter,
        string methodSpec,
        string[] newOrder,
        string? solution,
        bool dryRun)
    {
        var (workspace, loadedSolution) = await sessionFactory.LoadWorkspaceAsync(solution);
        using (workspace)
        {
            try
            {
                var updatedSolution = await changeSignature.ReorderParamsAsync(
                    loadedSolution,
                    methodSpec,
                    newOrder);

                await solutionChangeWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                return CommandHandlerConstants.SuccessExitCode;
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                return CommandHandlerConstants.ErrorExitCode;
            }
        }
    }

    /// <summary>
    /// Adds a parameter to a command signature.
    /// </summary>
    internal static async Task<int> AddParamAsync(
        IWorkspaceSessionFactory sessionFactory,
        ChangeSignatureService changeSignature,
        ISolutionChangeWriter solutionChangeWriter,
        string methodSpec,
        string paramSpec,
        string? after,
        string? solution,
        bool dryRun)
    {
        var (workspace, loadedSolution) = await sessionFactory.LoadWorkspaceAsync(solution);
        using (workspace)
        {
            try
            {
                var updatedSolution = await changeSignature.AddParamAsync(
                    loadedSolution,
                    methodSpec,
                    paramSpec,
                    after);

                await solutionChangeWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                return CommandHandlerConstants.SuccessExitCode;
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                return CommandHandlerConstants.ErrorExitCode;
            }
        }
    }

    /// <summary>
    /// Removes a parameter from a command signature.
    /// </summary>
    internal static async Task<int> RemoveParamAsync(
        IWorkspaceSessionFactory sessionFactory,
        ChangeSignatureService changeSignature,
        ISolutionChangeWriter solutionChangeWriter,
        string methodSpec,
        string paramName,
        bool force,
        string? solution,
        bool dryRun)
    {
        var (workspace, loadedSolution) = await sessionFactory.LoadWorkspaceAsync(solution);
        using (workspace)
        {
            try
            {
                var updatedSolution = await changeSignature.RemoveParamAsync(
                    loadedSolution,
                    methodSpec,
                    paramName,
                    force);

                await solutionChangeWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
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
