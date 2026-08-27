using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for constructor dependency injection.</summary>
internal sealed record AddDependencyRequest(
    [property: JsonRequired, CommandPropertyDescription("Target class name")] string Type,
    [property: JsonRequired, JsonPropertyName("dependency"), CommandPropertyDescription("Dependency type to inject (e.g. IEmailService)")]
    string Dependency,
    [property: CommandPropertyDescription("Field name override (default: derived from type)")]
    string? Field = null,
    [property: CommandPropertyDescription("Path to .sln/.slnx")]
    string? Solution = null,
    [property: JsonPropertyName("dry-run"), CommandPropertyDescription("Preview without writing (default)")]
    bool DryRun = true);

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
internal sealed class AddDependencyOperation(
    IWorkspaceSessionFactory sessionFactory,
    AddDependencyService addDependency,
    ISolutionChangeWriter solutionChangeWriter)
    : CommandApplicationPort<AddDependencyRequest, MutationExecutionResult<AddDependencyResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly AddDependencyService _addDependency = addDependency;
    private readonly PreparedChangeSetExecutor _changeSetExecutor = new(
        new SolutionPreparedChangeSetWriter(solutionChangeWriter));

    public override ApplicationCommandDefinition Definition => AddDependencyCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<AddDependencyResult>, RtError>> ExecuteAsync(
        AddDependencyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await _addDependency.PrepareAsync(
                solution, request.Type, request.Dependency, request.Field, cancellationToken);

            var result = new AddDependencyResult(
                prepared.Outcome,
                prepared.TargetType,
                prepared.Message);

            if (prepared.Changed)
                return Result<MutationExecutionResult<AddDependencyResult>, RtError>.Ok(
                    await ExecutePreparedChangeAsync(
                        solution,
                        prepared,
                        request.DryRun,
                        result,
                        cancellationToken));

            var diagnostic = CreateDiagnostic(prepared);
            if (diagnostic.Severity == MutationDiagnosticSeverity.Error)
                return Result<MutationExecutionResult<AddDependencyResult>, RtError>.Err(
                    CreateError(prepared));

            return Result<MutationExecutionResult<AddDependencyResult>, RtError>.Ok(
                MutationExecutionResult<AddDependencyResult>.NoOp(result, [diagnostic]));
        }
    }

    private async Task<MutationExecutionResult<AddDependencyResult>> ExecutePreparedChangeAsync(
        Solution originalSolution,
        AddDependencyService.DependencyInjectionResult prepared,
        bool dryRun,
        AddDependencyResult result,
        CancellationToken cancellationToken)
    {
        var changeSet = new PreparedChangeSet(originalSolution, prepared.Solution);
        return dryRun
            ? _changeSetExecutor.Preview(result, changeSet)
            : await _changeSetExecutor.ApplyAsync(result, changeSet, cancellationToken: cancellationToken);
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

    private static RtError CreateError(AddDependencyService.DependencyInjectionResult result)
    {
        var message = result.Message ?? result.Outcome.ToString();
        var context = result.TargetType is null
            ? null
            : new Dictionary<string, string> { ["targetType"] = result.TargetType };
        return result.Outcome switch
        {
            AddDependencyOutcome.TargetNotFound => RtError.NotFound(message, context),
            AddDependencyOutcome.TargetAmbiguous => RtError.AmbiguousMatch(message, context),
            _ => new RtError(RtErrorKind.InvalidInput, message, context)
        };
    }
}
