using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;

namespace RoslynTools.Services;

[JsonConverter(typeof(JsonStringEnumConverter<MoveMode>))]
internal enum MoveMode
{
    TypeToNamespace,
    NamespaceToNamespace,
    TypeToProject
}

internal sealed record MoveRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription(
        "Move mode: TypeToNamespace, NamespaceToNamespace, or TypeToProject")]
    MoveMode Mode,
    [property: CommandPropertyDescription("Fully-qualified type for type-based move modes")]
    string? Type = null,
    [property: JsonPropertyName("namespace")]
    [property: CommandPropertyDescription("Source namespace for NamespaceToNamespace mode")]
    string? Namespace = null,
    [property: JsonPropertyName("to-namespace")]
    [property: CommandPropertyDescription("Target namespace for namespace-changing modes")]
    string? ToNamespace = null,
    [property: JsonPropertyName("to-project")]
    [property: CommandPropertyDescription("Target project for TypeToProject mode")]
    string? ToProject = null,
    [property: JsonPropertyName("move-file")]
    [property: CommandPropertyDescription(
        "Move declaration files to folders matching the target namespace")]
    bool MoveFile = false,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal abstract record MoveIntent(MoveMode Mode);

internal sealed record TypeToNamespaceMove(
    string Type,
    string TargetNamespace,
    bool MoveFile) : MoveIntent(MoveMode.TypeToNamespace);

internal sealed record NamespaceToNamespaceMove(
    string SourceNamespace,
    string TargetNamespace,
    bool MoveFiles) : MoveIntent(MoveMode.NamespaceToNamespace);

internal sealed record TypeToProjectMove(
    string Type,
    string TargetProject,
    string? TargetNamespace) : MoveIntent(MoveMode.TypeToProject);

internal sealed record MoveResult(
    MoveMode Mode,
    IReadOnlyList<MoveTypeResult> MovedTypes,
    IReadOnlyList<string> SourceProjects,
    string? TargetProject,
    string SourceNamespace,
    string? TargetNamespace,
    int UpdatedReferenceDocumentCount,
    IReadOnlyList<MoveFileResult> FileMoves,
    IReadOnlyList<MoveProjectEditResult> ProjectEdits);

internal sealed record MoveTypeResult(
    string FullyQualifiedName,
    bool WasExplicitlyRequested,
    string DeclarationPath);

internal sealed record MoveFileResult(string SourcePath, string DestinationPath);

internal sealed record MoveProjectEditResult(string ProjectPath, string Description);

internal sealed record MovePreparation(
    Solution Solution,
    MoveResult Result,
    IReadOnlyList<PreparedFileMove> FileMoves,
    IReadOnlyList<PreparedFileWrite> ProjectFileEdits,
    IReadOnlyList<MutationDiagnostic> Diagnostics)
{
    internal PreparedChangeSet ToChangeSet(Solution originalSolution) => new(
        originalSolution,
        Solution,
        movedFiles: FileMoves,
        projectFileEdits: ProjectFileEdits);
}

internal static class MoveRequestClassifier
{
    internal static Result<MoveIntent, RtError> Classify(MoveRequest request)
    {
        var error = request.Mode switch
        {
            MoveMode.TypeToNamespace => ValidateTypeToNamespace(request),
            MoveMode.NamespaceToNamespace => ValidateNamespaceToNamespace(request),
            MoveMode.TypeToProject => ValidateTypeToProject(request),
            _ => $"Unsupported move mode '{request.Mode}'."
        };
        if (error is not null)
            return Result<MoveIntent, RtError>.Err(new RtError(RtErrorKind.InvalidInput, error));

        MoveIntent intent = request.Mode switch
        {
            MoveMode.TypeToNamespace => new TypeToNamespaceMove(
                request.Type!,
                request.ToNamespace!,
                request.MoveFile),
            MoveMode.NamespaceToNamespace => new NamespaceToNamespaceMove(
                request.Namespace!,
                request.ToNamespace!,
                request.MoveFile),
            MoveMode.TypeToProject => new TypeToProjectMove(
                request.Type!,
                request.ToProject!,
                request.ToNamespace),
            _ => throw new InvalidOperationException()
        };
        return Result<MoveIntent, RtError>.Ok(intent);
    }

    private static string? ValidateTypeToNamespace(MoveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
            return "TypeToNamespace requires type.";
        if (string.IsNullOrWhiteSpace(request.ToNamespace))
            return "TypeToNamespace requires to-namespace.";
        if (request.Namespace is not null || request.ToProject is not null)
            return "TypeToNamespace forbids namespace and to-project.";
        return ValidateNamespace(request.ToNamespace);
    }

    private static string? ValidateNamespaceToNamespace(MoveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Namespace))
            return "NamespaceToNamespace requires namespace.";
        if (string.IsNullOrWhiteSpace(request.ToNamespace))
            return "NamespaceToNamespace requires to-namespace.";
        if (request.Type is not null || request.ToProject is not null)
            return "NamespaceToNamespace forbids type and to-project.";
        return ValidateNamespace(request.Namespace) ?? ValidateNamespace(request.ToNamespace);
    }

    private static string? ValidateTypeToProject(MoveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
            return "TypeToProject requires type.";
        if (string.IsNullOrWhiteSpace(request.ToProject))
            return "TypeToProject requires to-project.";
        if (request.Namespace is not null || request.MoveFile)
            return "TypeToProject forbids namespace and move-file.";
        return request.ToNamespace is null ? null : ValidateNamespace(request.ToNamespace);
    }

    private static string? ValidateNamespace(string value)
    {
        if (value.Split('.').Any(part => !SyntaxFacts.IsValidIdentifier(part)))
            return $"'{value}' is not a valid namespace.";
        return null;
    }
}

/// <summary>Shared code-move operation used by CLI, daemon, and MCP.</summary>
internal sealed class MoveOperation(
    IWorkspaceSessionFactory sessions,
    MoveService service,
    PreparedChangeSetExecutor executor)
    : CommandApplicationPort<MoveRequest, MutationExecutionResult<MoveResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor = executor;
    private readonly IWorkspaceSessionFactory _sessions = sessions;
    private readonly MoveService _service = service;

    public override ApplicationCommandDefinition Definition => MoveCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<MoveResult>, RtError>> ExecuteAsync(
        MoveRequest request,
        CancellationToken cancellationToken = default)
    {
        var intentResult = MoveRequestClassifier.Classify(request);
        MoveIntent? intent = null;
        RtError? classificationError = null;
        intentResult.Match(value => intent = value, error => classificationError = error);
        if (classificationError is not null)
            return Result<MutationExecutionResult<MoveResult>, RtError>.Err(classificationError);

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var preparationResult = await _service.PrepareAsync(
                solution,
                intent!,
                cancellationToken);
            MovePreparation? preparation = null;
            RtError? preparationError = null;
            preparationResult.Match(value => preparation = value, error => preparationError = error);
            if (preparationError is not null)
                return Result<MutationExecutionResult<MoveResult>, RtError>.Err(preparationError);

            var changeSet = preparation!.ToChangeSet(solution);
            if (!changeSet.HasChanges)
                return Result<MutationExecutionResult<MoveResult>, RtError>.Ok(
                    MutationExecutionResult<MoveResult>.NoOp(
                        preparation.Result,
                        preparation.Diagnostics));

            var execution = request.DryRun
                ? _executor.Preview(preparation.Result, changeSet, preparation.Diagnostics)
                : await _executor.ApplyAsync(
                    preparation.Result,
                    changeSet,
                    preparation.Diagnostics,
                    cancellationToken);
            return Result<MutationExecutionResult<MoveResult>, RtError>.Ok(execution);
        }
    }
}

internal static class MoveCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "move",
        true,
        false,
        "refactor",
        WhenToUse:
        "Use this instead of manual Edit when moving a type or namespace while preserving bound references and reviewing every file and project edit before application. Use --move-file only for namespace moves; --to-project performs the physical cross-project move and forbids --move-file.",
        Examples:
        [
            "rt refactor move --type ProjectX.Core.BookingService --to-namespace ProjectX.Domain --dry-run",
            "rt refactor move --namespace ProjectX.Core.Models --to-namespace ProjectX.Domain.Entities --move-file --dry-run",
            "rt refactor move --type MyApp.Api.Product --to-project MyApp.Domain --to-namespace MyApp.Domain.Models --dry-run"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(MoveRequest),
        typeof(MutationExecutionResult<MoveResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
