using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for constructor dependency injection.</summary>
internal sealed record AddDependencyRequest(
    [property: JsonRequired] string Type,
    [property: JsonRequired, JsonPropertyName("dependency")] string Dependency,
    string? Field = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

/// <summary>One of the domain outcomes for an add-dependency request.</summary>
internal enum AddDependencyOutcome
{
    Applied,
    AlreadyInjected,
    TargetNotFound,
    TargetAmbiguous,
    Unsupported
}

/// <summary>
/// Structured mutation outcome returned before a host renders it. File paths are supplied
/// by the service so terminal and MCP presentations cannot disagree about the affected files.
/// </summary>
internal sealed record AddDependencyResult(
    AddDependencyOutcome Outcome,
    string? TargetType,
    string? Message);

/// <summary>Daemon-owned add-dependency application operation.</summary>
internal sealed class AddDependencyOperation
    : ICommandApplicationPort, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly AddDependencyService _addDependency;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public AddDependencyOperation(
        IWorkspaceSessionFactory sessionFactory,
        AddDependencyService addDependency,
        ISolutionChangeWriter solutionChangeWriter)
    {
        _sessionFactory = sessionFactory;
        _addDependency = addDependency;
        _solutionChangeWriter = solutionChangeWriter;
    }

    public string CommandName => "add-dependency";
    public Type RequestType => typeof(AddDependencyRequest);

    internal async Task<MutationExecutionResult<AddDependencyResult>> ExecuteAsync(
        AddDependencyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await _addDependency.PrepareAsync(
                solution, request.Type, request.Dependency, request.Field);

            return new AddDependencyResult(
                prepared.Outcome,
                prepared.TargetType,
                prepared.Message) switch
            {
                var result when prepared.Changed => await WriteAndCreateResultAsync(
                    solution, prepared, request.DryRun, result),
                var result => MutationExecutionResult<AddDependencyResult>.NoOp(
                    result,
                    [CreateDiagnostic(prepared)])
            };
        }
    }

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        if (request is not AddDependencyRequest typedRequest)
            throw new ArgumentException("Invalid add-dependency request.", nameof(request));

        var result = await ExecuteAsync(typedRequest, cancellationToken);
        var errors = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == MutationDiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Message)
            .ToArray();

        return errors.Length == 0
            ? CommandPortResult.Success(result)
            : CommandPortResult.Failure(string.Join(Environment.NewLine, errors));
    }

    private async Task<MutationExecutionResult<AddDependencyResult>> WriteAndCreateResultAsync(
        Solution originalSolution,
        AddDependencyService.DependencyInjectionResult prepared,
        bool dryRun,
        AddDependencyResult result)
    {
        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, prepared.Solution, dryRun);
        var changedFiles = prepared.ChangedFiles ?? [];
        return dryRun
            ? MutationExecutionResult<AddDependencyResult>.DryRun(result, changedFiles)
            : MutationExecutionResult<AddDependencyResult>.Applied(result, changedFiles);
    }

    private static MutationDiagnostic CreateDiagnostic(AddDependencyService.DependencyInjectionResult result)
    {
        var severity = result.Outcome is AddDependencyOutcome.TargetNotFound
            or AddDependencyOutcome.TargetAmbiguous
            or AddDependencyOutcome.Unsupported
            ? MutationDiagnosticSeverity.Error
            : MutationDiagnosticSeverity.Information;
        return new MutationDiagnostic(severity, result.Message ?? result.Outcome.ToString());
    }
}
