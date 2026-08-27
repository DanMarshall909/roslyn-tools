using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Typed input for the transport-neutral make-sealed operation.</summary>
internal sealed record MakeSealedRequest(
    [property: JsonRequired] string Type,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

/// <summary>The semantic outcome of attempting to seal a class.</summary>
internal enum MakeSealedOutcome
{
    Sealed,
    AlreadySealed,
    Abstract
}

/// <summary>
///     Structured mutation result produced before a host chooses how to render a diff,
///     no-op explanation, or applied-change confirmation.
/// </summary>
internal sealed record MakeSealedResult(
    string Type,
    MakeSealedOutcome Outcome,
    string? Message);

/// <summary>Prepared domain mutation before the daemon applies or previews it.</summary>
internal sealed record MakeSealedPreparation(
    Microsoft.CodeAnalysis.Solution Solution,
    MakeSealedResult Result,
    IReadOnlyList<string> ChangedFiles)
{
    internal bool Changed => ChangedFiles.Count > 0;
}

/// <summary>Shared make-sealed operation used by future CLI and MCP adapters.</summary>
internal sealed class MakeSealedOperation
    : LegacyCommandApplicationPort<MakeSealedRequest, MutationExecutionResult<MakeSealedResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly MakeSealedService _makeSealed;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public MakeSealedOperation(
        IWorkspaceSessionFactory sessionFactory,
        MakeSealedService makeSealed,
        ISolutionChangeWriter solutionChangeWriter)
    {
        _sessionFactory = sessionFactory;
        _makeSealed = makeSealed;
        _solutionChangeWriter = solutionChangeWriter;
    }

    public override string CommandName => "make-sealed";

    public override async Task<MutationExecutionResult<MakeSealedResult>> ExecuteAsync(
        MakeSealedRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await _makeSealed.PrepareAsync(solution, request.Type);
            if (!prepared.Changed)
            {
                return MutationExecutionResult<MakeSealedResult>.NoOp(
                    prepared.Result,
                    [new MutationDiagnostic(
                        MutationDiagnosticSeverity.Information,
                        prepared.Result.Message ?? "No changes were required.")]);
            }

            await _solutionChangeWriter.ApplyChangesAsync(
                solution,
                prepared.Solution,
                request.DryRun);

            return request.DryRun
                ? MutationExecutionResult<MakeSealedResult>.DryRun(
                    prepared.Result,
                    prepared.ChangedFiles)
                : MutationExecutionResult<MakeSealedResult>.Applied(
                    prepared.Result,
                    prepared.ChangedFiles);
        }
    }
}
