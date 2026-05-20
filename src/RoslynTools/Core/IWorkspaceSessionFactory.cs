using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
///     The injection seam for solution loading. Production code depends on this
///     interface rather than the concrete <see cref="WorkspaceLoader" /> so test
///     code can stub the seam and the daemon can cache an already-loaded workspace.
/// </summary>
internal interface IWorkspaceSessionFactory
{
    /// <summary>
    ///     Load and return the solution. The implementation owns the underlying
    ///     <see cref="Workspace" /> for its lifetime; callers must not dispose it.
    /// </summary>
    Task<Solution> GetSolutionAsync(string? solutionPath);

    /// <summary>
    ///     Compatibility load that yields both the workspace and the solution for
    ///     callers that still need workspace ownership while they migrate to
    ///     solution-only operations.
    /// </summary>
    Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath);
}
