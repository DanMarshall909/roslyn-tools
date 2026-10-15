namespace RoslynTools.Core;

/// <summary>Exact symbols and declared behavior for a bounded dependency introduction.</summary>
internal sealed record StaticDependencyIntroductionMapping(
    string SchemaVersion,
    StaticDependencySymbol Source,
    StaticDependencySymbol ReceiverType,
    StaticDependencySymbol Target,
    StaticDependencyPreconditions Preconditions);

/// <summary>An existing caller parameter, not an arbitrary replacement expression.</summary>
internal sealed record StaticDependencyCallerParameter(
    string ProjectPath,
    string MethodDocumentationId,
    int ParameterOrdinal);

internal sealed record StaticDependencyIntroductionRequest(
    StaticDependencyIntroductionMapping Mapping,
    StaticDependencySelection Selection,
    string ExpectedSourceFingerprint,
    string PreferredFieldName,
    string PreferredParameterName,
    IReadOnlyList<StaticDependencyCallerParameter> Callers,
    string? ExistingReceiverFieldDocumentationId = null);

internal enum StaticDependencyClosureChangeKind
{
    Field,
    ConstructorParameter,
    ConstructorAssignment,
    ConstructorForwarding,
    ConstructionArgument,
    MappedCall
}

/// <summary>A complete closure edit with original location and resolved candidate identity.</summary>
internal sealed record StaticDependencyClosureChange(
    string Id,
    StaticDependencyClosureChangeKind Kind,
    string ProjectPath,
    string Path,
    int SpanStart,
    int SpanLength,
    string? OriginalSymbolDocumentationId,
    string CandidateSymbolDocumentationId,
    string Detail);

internal sealed record StaticDependencyIntroductionResolution(
    bool ReusedExistingReceiver,
    string FieldDocumentationId,
    string FieldName,
    string? ParameterName);

/// <summary>A no-write proposal relative to the acquired original solution.</summary>
internal sealed record StaticDependencyIntroductionPlan(
    string SchemaVersion,
    string SourceFingerprint,
    string MappingFingerprint,
    StaticDependencyIntroductionRequest Request,
    StaticDependencyPlanStatus Status,
    IReadOnlyList<StaticDependencySite> Sites,
    IReadOnlyList<StaticDependencyDocumentChange> Documents,
    IReadOnlyList<StaticDependencyClosureChange> Closure,
    StaticDependencyIntroductionResolution? Resolution,
    PreparedChangeSet Changes);
