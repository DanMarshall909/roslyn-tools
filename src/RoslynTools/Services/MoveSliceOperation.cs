using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record MoveSliceRequest(
    [property: CommandPropertyDescription(
        "Fully-qualified source types; repeat for multiple types. Mutually exclusive with namespace.")]
    IReadOnlyList<string>? Types,
    [property: JsonPropertyName("namespace")]
    [property: CommandPropertyDescription(
        "Recursive source namespace root. Mutually exclusive with types.")]
    string? Namespace,
    [property: JsonRequired, JsonPropertyName("to-project")]
    [property: CommandPropertyDescription("Target project name")]
    string ToProject,
    [property: JsonRequired, JsonPropertyName("to-namespace")]
    [property: CommandPropertyDescription("Target namespace root")]
    string ToNamespace,
    [property: JsonPropertyName("analyze-dependencies")]
    [property: CommandPropertyDescription(
        "Report required related source-project types without adding them to the slice")]
    bool AnalyzeDependencies = false,
    [property: JsonPropertyName("update-project-references")]
    [property: CommandPropertyDescription(
        "Allow only the project-reference additions listed in the reviewed manifest")]
    bool UpdateProjectReferences = false,
    [property: JsonPropertyName("project-reference-justification")]
    [property: CommandPropertyDescription(
        "Required justification when update-project-references is true")]
    string? ProjectReferenceJustification = null,
    [property: JsonPropertyName("add-internals-visible-to")]
    [property: CommandPropertyDescription(
        "Explicit Project=Assembly friend declarations to add; repeat for multiple edits")]
    IReadOnlyList<string>? AddInternalsVisibleTo = null,
    [property: JsonPropertyName("internals-visible-to-justification")]
    [property: CommandPropertyDescription(
        "Required justification when add-internals-visible-to is supplied")]
    string? InternalsVisibleToJustification = null,
    [property: CommandPropertyDescription("Path to .sln/.slnx")]
    string? Solution = null,
    [property: JsonPropertyName("dry-run")]
    [property: CommandPropertyDescription("Preview the complete manifest without writing (default)")]
    bool DryRun = true);

internal enum MoveSliceSelectionKind
{
    Types,
    Namespace
}

internal sealed record MoveSliceIntent(
    MoveSliceSelectionKind SelectionKind,
    IReadOnlyList<string> Types,
    string? SourceNamespace,
    string TargetProject,
    string TargetNamespace,
    bool AnalyzeDependencies,
    bool UpdateProjectReferences,
    string? ProjectReferenceJustification,
    IReadOnlyList<InternalsVisibleToEditRequest> InternalsVisibleToEdits,
    string? InternalsVisibleToJustification);

internal sealed record InternalsVisibleToEditRequest(string Project, string Assembly);

internal static class MoveSliceRequestClassifier
{
    internal static Result<MoveSliceIntent, RtError> Classify(MoveSliceRequest request)
    {
        var types = request.Types?
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray() ?? [];
        var hasNamespace = !string.IsNullOrWhiteSpace(request.Namespace);
        if ((types.Length == 0) == !hasNamespace)
            return Invalid("Supply exactly one of types or namespace.");
        if (string.IsNullOrWhiteSpace(request.ToProject))
            return Invalid("to-project is required.");
        if (string.IsNullOrWhiteSpace(request.ToNamespace))
            return Invalid("to-namespace is required.");

        var namespaceError = ValidateNamespace(request.ToNamespace)
                             ?? (hasNamespace ? ValidateNamespace(request.Namespace!) : null);
        if (namespaceError is not null)
            return Invalid(namespaceError);
        if (request.UpdateProjectReferences
            && string.IsNullOrWhiteSpace(request.ProjectReferenceJustification))
            return Invalid(
                "project-reference-justification is required when update-project-references is true.");
        if (!request.UpdateProjectReferences
            && request.ProjectReferenceJustification is not null)
            return Invalid(
                "project-reference-justification requires update-project-references.");

        var friendEdits = new List<InternalsVisibleToEditRequest>();
        foreach (var value in request.AddInternalsVisibleTo ?? [])
        {
            var separator = value.IndexOf('=');
            if (separator <= 0 || separator == value.Length - 1)
                return Invalid(
                    $"InternalsVisibleTo edit '{value}' must use Project=Assembly syntax.");
            friendEdits.Add(new InternalsVisibleToEditRequest(
                value[..separator].Trim(),
                value[(separator + 1)..].Trim()));
        }

        if (friendEdits.Count != 0
            && string.IsNullOrWhiteSpace(request.InternalsVisibleToJustification))
            return Invalid(
                "internals-visible-to-justification is required when add-internals-visible-to is supplied.");
        if (friendEdits.Count == 0
            && request.InternalsVisibleToJustification is not null)
            return Invalid(
                "internals-visible-to-justification requires add-internals-visible-to.");

        return Result<MoveSliceIntent, RtError>.Ok(new MoveSliceIntent(
            hasNamespace ? MoveSliceSelectionKind.Namespace : MoveSliceSelectionKind.Types,
            types,
            hasNamespace ? request.Namespace!.Trim() : null,
            request.ToProject.Trim(),
            request.ToNamespace.Trim(),
            request.AnalyzeDependencies,
            request.UpdateProjectReferences,
            request.ProjectReferenceJustification?.Trim(),
            friendEdits
                .Distinct()
                .OrderBy(edit => edit.Project, StringComparer.Ordinal)
                .ThenBy(edit => edit.Assembly, StringComparer.Ordinal)
                .ToArray(),
            request.InternalsVisibleToJustification?.Trim()));
    }

    private static Result<MoveSliceIntent, RtError> Invalid(string message) =>
        Result<MoveSliceIntent, RtError>.Err(new RtError(RtErrorKind.InvalidInput, message));

    private static string? ValidateNamespace(string value) =>
        value.Split('.').Any(part => !SyntaxFacts.IsValidIdentifier(part))
            ? $"'{value}' is not a valid namespace."
            : null;
}

internal enum MoveSliceTypeSelectionReason
{
    Explicit,
    Namespace,
    CoLocatedDeclaration,
    PartialDeclaration
}

internal sealed record MoveSliceSelection(
    MoveSliceSelectionKind Kind,
    IReadOnlyList<string> Types,
    string? Namespace);

internal sealed record MoveSliceTypeManifest(
    string SourceType,
    string TargetType,
    MoveSliceTypeSelectionReason SelectionReason,
    IReadOnlyList<string> DeclarationPaths);

internal sealed record MoveSliceDocumentEdit(
    string Path,
    string Description);

internal enum MoveSliceProjectEditKind
{
    CompileItem,
    ProjectReference,
    InternalsVisibleTo
}

internal sealed record MoveSliceProjectEdit(
    MoveSliceProjectEditKind Kind,
    string Project,
    string ProjectPath,
    string Description,
    bool ExplicitlyRequested,
    string? Justification);

internal sealed record MoveSliceDependencyEvidence(
    string File,
    int Line,
    string Dependency);

internal sealed record MoveSliceRelatedType(
    string Type,
    string Project,
    IReadOnlyList<MoveSliceDependencyEvidence> Evidence);

internal sealed record MoveSliceResult(
    MoveSliceSelection Selection,
    string SourceProject,
    string TargetProject,
    string TargetNamespace,
    bool Applicable,
    IReadOnlyList<MoveSliceTypeManifest> Types,
    IReadOnlyList<MoveSliceDocumentEdit> SourceEdits,
    IReadOnlyList<MoveSliceDocumentEdit> ReferenceEdits,
    IReadOnlyList<MoveFileResult> FileMoves,
    IReadOnlyList<MoveSliceProjectEdit> ProjectEdits,
    IReadOnlyList<MoveSliceRelatedType> RequiredRelatedTypes,
    int ChangedDocumentCount);

internal static class MoveSliceCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "move-slice",
        true,
        false,
        "refactor",
        WhenToUse:
        "Use this instead of a sequence of manual Edit and file moves when relocating a coherent architectural slice across projects and namespaces, after reviewing its complete manifest.",
        Examples:
        [
            "rt refactor move-slice --type Legacy.App.Service --type Legacy.App.Result --to-project App.Application --to-namespace App.Application.Feature --analyze-dependencies --dry-run",
            "rt refactor move-slice --namespace Legacy.Domain.Payments --to-project App.Domain --to-namespace App.Domain.Payments --dry-run"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(MoveSliceRequest),
        typeof(MutationExecutionResult<MoveSliceResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}

