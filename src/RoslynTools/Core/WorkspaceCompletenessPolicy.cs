using System.Collections.Immutable;

namespace RoslynTools.Core;

internal sealed record WorkspaceCompletenessDecision(
    WorkspaceCompletenessState State,
    ImmutableArray<string> Reasons);

/// <summary>Central fail-closed policy; successful loaded compilations never substitute for acquisition evidence.</summary>
internal static class WorkspaceCompletenessPolicy
{
    internal static WorkspaceCompletenessDecision Evaluate(
        SemanticInputManifest manifest,
        IReadOnlyCollection<SemanticInputOutcome> outcomes,
        IReadOnlyCollection<WorkspaceAcquisitionDiagnostic> diagnostics,
        bool hasUsableRequestedScope,
        bool snapshotMatches)
    {
        var reasons = ImmutableArray.CreateBuilder<string>();
        var minimumUnavailable = !hasUsableRequestedScope;
        if (minimumUnavailable)
            reasons.Add("No usable semantic model exists within the declared requested scope.");
        if (manifest.DiscoveryState != ManifestDiscoveryState.Closed)
            reasons.Add("Expected semantic-input discovery has not been closed by an authoritative collector.");
        if (!snapshotMatches)
            reasons.Add("Acquisition evidence is absent or does not match this solution snapshot and source fingerprint.");

        var grouped = outcomes.GroupBy(outcome => outcome.InputId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (grouped.Values.Any(matches => matches.Length != 1))
            reasons.Add("Acquisition outcomes contain duplicate input identities, including optional inputs.");
        foreach (var input in manifest.Inputs.Where(input => input.Required || input.RequiredForMinimumScope))
        {
            var satisfied = grouped.TryGetValue(input.Id, out var matches) && matches.Length == 1
                && IsSatisfied(input, matches[0]);
            if (satisfied)
                continue;
            reasons.Add($"Required semantic input '{input.Id}' has no unique, satisfied acquisition outcome.");
            minimumUnavailable |= input.RequiredForMinimumScope;
        }
        foreach (var kind in new[] { SemanticInputKind.ManifestClosure, SemanticInputKind.EvaluationStage,
                     SemanticInputKind.GeneratorStage, SemanticInputKind.AnalyzerStage, SemanticInputKind.SourceSnapshot,
                     SemanticInputKind.WorkspaceInputSet, SemanticInputKind.Sdk, SemanticInputKind.Package,
                     SemanticInputKind.BuildInput, SemanticInputKind.FrameworkReference })
        {
            if (!manifest.Inputs.Any(input => input.Kind == kind && input.Required))
                reasons.Add($"The manifest has no required {kind} receipt.");
        }
        if (outcomes.Any(outcome => !manifest.Inputs.Any(input => input.Id == outcome.InputId)))
            reasons.Add("Observed outcomes include semantic inputs absent from the expected manifest.");

        foreach (var diagnostic in diagnostics.Where(diagnostic => diagnostic.Impact != CompletenessDiagnosticImpact.None))
        {
            reasons.Add($"Diagnostic '{diagnostic.Id}' has completeness impact '{diagnostic.Impact}'.");
            minimumUnavailable |= diagnostic.Impact == CompletenessDiagnosticImpact.MinimumScopeUnavailable;
        }
        return new WorkspaceCompletenessDecision(
            minimumUnavailable ? WorkspaceCompletenessState.Failed
                : reasons.Count > 0 ? WorkspaceCompletenessState.Partial
                : WorkspaceCompletenessState.Complete,
            reasons.ToImmutable());
    }

    private static bool IsSatisfied(ExpectedSemanticInput input, SemanticInputOutcome outcome)
        => (outcome.State == SemanticInputState.Resolved
                || outcome.State == SemanticInputState.Loaded && input.Kind is not (SemanticInputKind.ManifestClosure
                    or SemanticInputKind.EvaluationStage or SemanticInputKind.GeneratorStage or SemanticInputKind.AnalyzerStage
                    or SemanticInputKind.SourceSnapshot or SemanticInputKind.WorkspaceInputSet)
                || outcome.State == SemanticInputState.Excluded && outcome.ExclusionValidated
                    && input.Kind is not (SemanticInputKind.ManifestClosure or SemanticInputKind.SourceSnapshot
                        or SemanticInputKind.WorkspaceInputSet))
            && (input.Fingerprint is null
                || string.Equals(input.Fingerprint, outcome.ObservedFingerprint, StringComparison.Ordinal));
}
