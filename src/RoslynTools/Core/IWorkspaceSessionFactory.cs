using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
///     Caller-owned workspace snapshot plus the authoritative acquisition and semantic
///     completeness assessment established at the application boundary.
/// </summary>
internal sealed record WorkspaceLoadResult(
    Workspace Workspace,
    Solution Solution,
    WorkspaceCompleteness Completeness);

/// <summary>
///     The injection seam for solution loading. Production code depends on this
///     interface rather than the concrete <see cref="WorkspaceLoader" /> so test
///     code can stub the seam and the daemon can cache an already-loaded workspace.
/// </summary>
internal interface IWorkspaceSessionFactory : IReadOnlyWorkspaceSessions
{
    /// <summary>
    ///     Reports whether tests have installed an ambient workspace. Keeping the
    ///     compatibility hook behind this seam prevents daemon routing code from
    ///     depending on the concrete workspace loader.
    /// </summary>
    static bool HasAmbientOverride => WorkspaceLoader.Override.Value is not null;

    /// <summary>
    ///     Returns the ambient test snapshot, when one has been installed. Production host
    ///     invocations do not set this compatibility hook.
    /// </summary>
    static (Workspace Workspace, Solution Solution)? AmbientOverride => WorkspaceLoader.Override.Value;

    /// <summary>
    ///     The sanctioned default factory for code paths that may run without an
    ///     IServiceProvider (e.g. tests that call <c>App.RunAsync</c> with
    ///     <c>services: null</c>). Centralising the construction here is what
    ///     lets the <c>NoConcreteWorkspaceLoaderInProduction</c> architecture
    ///     gate ban every other production reference to the concrete
    ///     <c>WorkspaceLoader</c>.
    /// </summary>
    static IWorkspaceSessionFactory CreateDefault() => new WorkspaceLoader();

    /// <summary>
    ///     Loads a caller-owned workspace and solution plus the authoritative initial
    ///     completeness assessment. Callers may reassess the same solution after deferred
    ///     semantic work and must dispose the returned workspace.
    /// </summary>
    /// <returns>The acquired workspace, its solution snapshot, and typed completeness evidence.</returns>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    /// <exception cref="RepositoryTrustRequiredException">Repository evaluation was not authorized.</exception>
    async Task<WorkspaceLoadResult> LoadWorkspaceResultAsync(
        string? solutionPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await LoadWorkspaceAsync(solutionPath);
        try
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, cancellationToken);
            return new WorkspaceLoadResult(workspace, solution, completeness);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Compatibility solution-only load. The implementation owns the underlying
    ///     <see cref="Workspace" /> for its lifetime; callers must not dispose it.
    /// </summary>
    Task<Solution> GetSolutionAsync(string? solutionPath);

    /// <summary>
    ///     Compatibility load that yields the workspace and solution without carrying
    ///     acquisition status. New application operations use <see cref="LoadWorkspaceResultAsync" />.
    ///     This member remains for callers that still need workspace ownership while they migrate to
    ///     solution-only operations.
    /// </summary>
    Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath);

    async Task<ReadOnlyWorkspaceLease> IReadOnlyWorkspaceSessions.AcquireAsync(
        string? solutionPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = await LoadWorkspaceResultAsync(solutionPath, cancellationToken);
        try
        {
            var revision = await SolutionRevision.ComputeAsync(loaded.Solution, cancellationToken);
            return new ReadOnlyWorkspaceLease(
                loaded.Solution,
                revision,
                loaded.Completeness,
                loaded.Workspace.Dispose);
        }
        catch
        {
            loaded.Workspace.Dispose();
            throw;
        }
    }
}
