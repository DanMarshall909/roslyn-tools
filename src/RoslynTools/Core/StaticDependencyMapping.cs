namespace RoslynTools.Core;

/// <summary>An exact symbol identity, including the assembly that declares it.</summary>
internal sealed record StaticDependencySymbol(string DocumentationId, string AssemblyIdentity);

/// <summary>Caller-declared behavior that semantic binding cannot prove.</summary>
internal sealed record StaticDependencyPreconditions(
    string ReceiverAvailability,
    string ReceiverLifetime,
    string ReceiverNonNull,
    string AdapterExceptions,
    string AdapterEncoding,
    string AdapterCancellation,
    string AdapterResourceOwnership,
    IReadOnlyList<string> ContractEvidence);

/// <summary>The v1 grammar replaces a static method with an existing field's method.</summary>
internal sealed record StaticDependencyMethodMapping(
    string SchemaVersion,
    StaticDependencySymbol Source,
    string ReceiverFieldDocumentationId,
    StaticDependencySymbol Target,
    StaticDependencyPreconditions Preconditions);

internal sealed record StaticDependencySelection(
    string ProjectPath,
    string TypeDocumentationId,
    IReadOnlyList<string>? SiteIds = null);

internal sealed record StaticDependencyMappingRequest(
    StaticDependencyMethodMapping Mapping,
    StaticDependencySelection Selection,
    string ExpectedSourceFingerprint);

internal enum StaticDependencyPlanStatus
{
    Prepared,
    NoOp,
    Blocked
}

internal enum StaticDependencySiteDisposition
{
    ConvertedInCandidate,
    Skipped,
    Blocked
}

internal sealed record StaticDependencySite(
    string Id,
    string ProjectPath,
    string TypeDocumentationId,
    string Path,
    int SpanStart,
    int SpanLength,
    StaticDependencySiteDisposition Disposition,
    string Code,
    string Message);

/// <summary>Complete source text on both sides of a proposed document change.</summary>
internal sealed record StaticDependencyDocumentChange(string Path, string Before, string After);

internal sealed record StaticDependencyPlan(
    string SchemaVersion,
    string SourceFingerprint,
    string MappingFingerprint,
    StaticDependencyMappingRequest Request,
    StaticDependencyPlanStatus Status,
    IReadOnlyList<StaticDependencySite> Sites,
    IReadOnlyList<StaticDependencyDocumentChange> Documents,
    PreparedChangeSet Changes);
