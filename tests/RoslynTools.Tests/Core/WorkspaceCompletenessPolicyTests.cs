using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class WorkspaceCompletenessPolicyTests
{
    [Fact]
    public void closed_explicit_manifest_with_all_outcomes_can_be_complete()
    {
        var manifest = Manifest();
        Assess(manifest, Resolved(manifest)).State.ShouldBe(WorkspaceCompletenessState.Complete);
    }

    [Theory]
    [InlineData("Project")]
    [InlineData("CompilationContext")]
    [InlineData("SourceDocument")]
    [InlineData("AdditionalDocument")]
    [InlineData("AnalyzerConfigDocument")]
    [InlineData("GeneratedDocument")]
    [InlineData("ProjectReference")]
    [InlineData("MetadataReference")]
    [InlineData("AnalyzerReference")]
    [InlineData("FrameworkReference")]
    [InlineData("Sdk")]
    [InlineData("Package")]
    [InlineData("BuildInput")]
    [InlineData("EvaluationStage")]
    [InlineData("GeneratorStage")]
    [InlineData("AnalyzerStage")]
    [InlineData("SourceSnapshot")]
    public void one_missing_required_outcome_is_partial_even_when_loaded_projects_compile(string kind)
    {
        var manifest = Manifest(new ExpectedSemanticInput("omitted", Enum.Parse<SemanticInputKind>(kind), true));
        var outcomes = Resolved(manifest).Where(outcome => outcome.InputId != "omitted").ToArray();
        Assess(manifest, outcomes).State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    [Fact]
    public void minimum_scope_failure_is_failed_despite_another_usable_project()
    {
        var manifest = Manifest(new ExpectedSemanticInput("minimum", SemanticInputKind.Project, true, RequiredForMinimumScope: true));
        var outcomes = Resolved(manifest).Where(outcome => outcome.InputId != "minimum").ToArray();
        Assess(manifest, outcomes).State.ShouldBe(WorkspaceCompletenessState.Failed);
    }

    [Fact]
    public void no_usable_requested_scope_is_failed()
    {
        var manifest = Manifest();
        WorkspaceCompletenessPolicy.Evaluate(manifest, Resolved(manifest), [],
            hasUsableRequestedScope: false, snapshotMatches: true).State.ShouldBe(WorkspaceCompletenessState.Failed);
    }

    [Fact]
    public void unclosed_discovery_is_partial_despite_every_known_outcome_being_satisfied()
    {
        var manifest = new SemanticInputManifest(null, Manifest().Inputs, ManifestDiscoveryState.Unknown);
        Assess(manifest, Resolved(manifest)).State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    [Fact]
    public void closed_flag_without_required_closure_receipt_is_not_complete()
    {
        var manifest = new SemanticInputManifest(null, [new ExpectedSemanticInput("project", SemanticInputKind.Project, true)],
            ManifestDiscoveryState.Closed);
        Assess(manifest, Resolved(manifest)).State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    [Theory]
    [InlineData("ManifestClosure")]
    [InlineData("EvaluationStage")]
    [InlineData("GeneratorStage")]
    [InlineData("AnalyzerStage")]
    [InlineData("SourceSnapshot")]
    public void mandatory_stage_cannot_be_omitted_or_merely_loaded(string kindName)
    {
        var kind = Enum.Parse<SemanticInputKind>(kindName);
        var complete = Manifest();
        var omitted = new SemanticInputManifest(null, complete.Inputs.Where(input => input.Kind != kind), ManifestDiscoveryState.Closed);
        Assess(omitted, Resolved(omitted)).State.ShouldBe(WorkspaceCompletenessState.Partial);
        Assess(complete, Resolved(complete).Select(outcome => complete.Inputs.Single(input => input.Id == outcome.InputId).Kind == kind
            ? outcome with { State = SemanticInputState.Loaded } : outcome).ToArray()).State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    [Theory]
    [InlineData("Sdk")]
    [InlineData("Package")]
    [InlineData("BuildInput")]
    [InlineData("FrameworkReference")]
    public void mandatory_build_scope_cannot_be_silently_omitted(string kindName)
    {
        var kind = Enum.Parse<SemanticInputKind>(kindName);
        var manifest = new SemanticInputManifest(null, Manifest().Inputs.Where(input => input.Kind != kind), ManifestDiscoveryState.Closed);
        Assess(manifest, Resolved(manifest)).State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    [Theory]
    [InlineData("ManifestClosure")]
    [InlineData("SourceSnapshot")]
    public void closure_and_snapshot_receipts_cannot_be_excluded(string kindName)
    {
        var kind = Enum.Parse<SemanticInputKind>(kindName);
        var manifest = Manifest();
        Assess(manifest, Resolved(manifest).Select(outcome => manifest.Inputs.Single(input => input.Id == outcome.InputId).Kind == kind
            ? outcome with { State = SemanticInputState.Excluded, ExclusionValidated = true } : outcome).ToArray())
            .State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    [Fact]
    public void stale_snapshot_or_input_fingerprint_is_partial()
    {
        var manifest = Manifest(new ExpectedSemanticInput("source", SemanticInputKind.SourceDocument, true, Fingerprint: "before"));
        Assess(manifest, Resolved(manifest)).State.ShouldBe(WorkspaceCompletenessState.Partial);
        WorkspaceCompletenessPolicy.Evaluate(Manifest(), Resolved(Manifest()), [],
            hasUsableRequestedScope: true, snapshotMatches: false).State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    [Fact]
    public void exclusions_need_validated_operation_irrelevance()
    {
        var manifest = Manifest(new ExpectedSemanticInput("analyzer", SemanticInputKind.AnalyzerStage, true));
        var resolved = Resolved(manifest).Where(outcome => outcome.InputId != "analyzer");
        Assess(manifest, resolved.Append(new SemanticInputOutcome("analyzer", SemanticInputState.Excluded)).ToArray())
            .State.ShouldBe(WorkspaceCompletenessState.Partial);
        Assess(manifest, resolved.Append(new SemanticInputOutcome("analyzer", SemanticInputState.Excluded,
            ExclusionValidated: true)).ToArray()).State.ShouldBe(WorkspaceCompletenessState.Complete);
    }

    [Fact]
    public void warning_severity_and_completeness_impact_are_independent()
    {
        var manifest = Manifest();
        var warning = new WorkspaceAcquisitionDiagnostic("opaque", DiagnosticSeverity.Warning,
            CompletenessDiagnosticImpact.Unknown, "workspace", "A localized diagnostic without English keywords.");
        Assess(manifest, Resolved(manifest), [warning]).State.ShouldBe(WorkspaceCompletenessState.Partial);
        Assess(manifest, Resolved(manifest), [warning with { Impact = CompletenessDiagnosticImpact.None }])
            .State.ShouldBe(WorkspaceCompletenessState.Complete);
    }

    [Fact]
    public void duplicate_or_undeclared_receipts_cannot_promote_completeness()
    {
        var manifest = Manifest();
        Assess(manifest, Resolved(manifest).Append(new SemanticInputOutcome("project", SemanticInputState.Resolved)).ToArray())
            .State.ShouldBe(WorkspaceCompletenessState.Partial);
        Assess(manifest, Resolved(manifest).Append(new SemanticInputOutcome("unexpected", SemanticInputState.Loaded)).ToArray())
            .State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    [Fact]
    public void duplicate_optional_outcomes_are_malformed_even_when_every_required_input_is_satisfied()
    {
        var manifest = Manifest(new ExpectedSemanticInput("optional", SemanticInputKind.AnalyzerStage, false));
        Assess(manifest, Resolved(manifest).Append(new SemanticInputOutcome("optional", SemanticInputState.Resolved)).ToArray())
            .State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    private static SemanticInputManifest Manifest(params ExpectedSemanticInput[] additionalInputs)
        => new(null,
            new[]
            {
                new ExpectedSemanticInput("closure", SemanticInputKind.ManifestClosure, true),
                new ExpectedSemanticInput("evaluation", SemanticInputKind.EvaluationStage, true),
                new ExpectedSemanticInput("generators", SemanticInputKind.GeneratorStage, true),
                new ExpectedSemanticInput("analyzers", SemanticInputKind.AnalyzerStage, true),
                new ExpectedSemanticInput("snapshot", SemanticInputKind.SourceSnapshot, true),
                new ExpectedSemanticInput("sdk", SemanticInputKind.Sdk, true),
                new ExpectedSemanticInput("package", SemanticInputKind.Package, true),
                new ExpectedSemanticInput("build", SemanticInputKind.BuildInput, true),
                new ExpectedSemanticInput("framework", SemanticInputKind.FrameworkReference, true),
                new ExpectedSemanticInput("project", SemanticInputKind.Project, true)
            }.Concat(additionalInputs), ManifestDiscoveryState.Closed);

    private static SemanticInputOutcome[] Resolved(SemanticInputManifest manifest)
        => manifest.Inputs.Select(input => new SemanticInputOutcome(input.Id, SemanticInputState.Resolved)).ToArray();

    private static WorkspaceCompletenessDecision Assess(SemanticInputManifest manifest,
        IReadOnlyCollection<SemanticInputOutcome> outcomes, IReadOnlyCollection<WorkspaceAcquisitionDiagnostic>? diagnostics = null)
        => WorkspaceCompletenessPolicy.Evaluate(manifest, outcomes, diagnostics ?? [],
            hasUsableRequestedScope: true, snapshotMatches: true);
}
