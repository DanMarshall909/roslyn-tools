using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

internal enum SemanticInputKind
{
    Project,
    CompilationContext,
    SourceDocument,
    AdditionalDocument,
    AnalyzerConfigDocument,
    GeneratedDocument,
    ProjectReference,
    MetadataReference,
    AnalyzerReference,
    FrameworkReference,
    Sdk,
    Package,
    BuildInput,
    EvaluationStage,
    GeneratorStage,
    AnalyzerStage,
    ManifestClosure,
    SourceSnapshot
}

internal enum SemanticInputState
{
    Unknown,
    Loaded,
    Resolved,
    Failed,
    PolicyBlocked,
    Excluded
}

internal enum ManifestDiscoveryState
{
    Unknown,
    Closed
}

internal enum CompletenessDiagnosticImpact
{
    None,
    RequiredInputIncomplete,
    MinimumScopeUnavailable,
    Unknown
}

/// <summary>An expected input declared independently of whichever Roslyn projects happen to load.</summary>
internal sealed record ExpectedSemanticInput(
    string Id,
    SemanticInputKind Kind,
    bool Required,
    bool RequiredForMinimumScope = false,
    string? ProjectPath = null,
    string? Context = null,
    string? Path = null,
    string? Fingerprint = null);

internal sealed record SemanticInputOutcome(
    string InputId,
    SemanticInputState State,
    string? ObservedFingerprint = null,
    string? Detail = null,
    bool ExclusionValidated = false);

internal sealed record WorkspaceAcquisitionDiagnostic(
    string Id,
    DiagnosticSeverity Severity,
    CompletenessDiagnosticImpact Impact,
    string Origin,
    string Message,
    string? InputId = null,
    string? ProjectPath = null);

/// <summary>Immutable request scope plus its discovery status; unknown discovery cannot support Complete.</summary>
internal sealed class SemanticInputManifest
{
    internal SemanticInputManifest(string? solutionPath, IEnumerable<ExpectedSemanticInput> inputs,
        ManifestDiscoveryState discoveryState = ManifestDiscoveryState.Unknown)
    {
        SolutionPath = solutionPath;
        Inputs = inputs.ToImmutableArray();
        if (Inputs.Any(input => string.IsNullOrWhiteSpace(input.Id))
            || Inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count() != Inputs.Length)
            throw new ArgumentException("Semantic input identifiers must be nonempty and unique.", nameof(inputs));
        DiscoveryState = discoveryState;
    }

    public string? SolutionPath { get; }
    public ImmutableArray<ExpectedSemanticInput> Inputs { get; }
    public ManifestDiscoveryState DiscoveryState { get; }
}

internal sealed record WorkspaceEvaluatorProvenance(
    string? RepositoryRoot,
    string? SolutionPath,
    string? SourceFingerprint,
    string Configuration,
    ImmutableArray<string> TargetFrameworks,
    string SdkIdentity,
    string CompilerIdentity,
    string AnalyzerGeneratorPolicy,
    string TrustState,
    string EvaluatorMode);

/// <summary>A frozen acquisition account for host-neutral output projections. It contains no live Roslyn workspace objects.</summary>
internal sealed record WorkspaceAcquisitionResult(
    WorkspaceCompletenessState State,
    SemanticInputManifest Manifest,
    ImmutableArray<SemanticInputOutcome> Outcomes,
    ImmutableArray<WorkspaceAcquisitionDiagnostic> Diagnostics,
    WorkspaceEvaluatorProvenance Provenance,
    ImmutableArray<string> Limitations);

/// <summary>
/// Collector evidence bound to one immutable Roslyn solution snapshot. Ordinary tool requests cannot supply receipts.
/// A complete fixture must explicitly supply its expected inputs, outcomes, and fingerprint.
/// </summary>
internal sealed class WorkspaceAcquisitionReceipt
{
    internal WorkspaceAcquisitionReceipt(Solution solution, SemanticInputManifest manifest,
        IEnumerable<SemanticInputOutcome> outcomes, WorkspaceEvaluatorProvenance provenance)
    {
        Solution = solution;
        Manifest = manifest;
        Outcomes = outcomes.ToImmutableArray();
        Provenance = provenance;
    }

    internal Solution Solution { get; }
    internal SemanticInputManifest Manifest { get; }
    internal ImmutableArray<SemanticInputOutcome> Outcomes { get; }
    internal WorkspaceEvaluatorProvenance Provenance { get; }
}
