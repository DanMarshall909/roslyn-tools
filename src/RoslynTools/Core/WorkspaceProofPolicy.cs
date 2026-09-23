namespace RoslynTools.Core;

/// <summary>
/// Central policy for conclusions and follow-on actions that depend on the absence of semantic evidence.
/// </summary>
internal static class WorkspaceProofPolicy
{
    internal static WorkspaceProofDecision Evaluate(
        WorkspaceCompleteness completeness,
        bool operationEvidenceSupportsDeletion = false)
    {
        var complete = completeness.State == WorkspaceCompletenessState.Complete;
        return new WorkspaceProofDecision(
            CanUseAbsenceEvidence: complete,
            CanOfferDeletionAdvice: complete && operationEvidenceSupportsDeletion,
            ConfidenceLevel: complete ? "High" : "Limited",
            Limitation: complete
                ? null
                : $"The workspace is {completeness.State}; findings are advisory, absent references or symbols are not proof, and deletion advice is blocked.");
    }
}

internal sealed record WorkspaceProofDecision(
    bool CanUseAbsenceEvidence,
    bool CanOfferDeletionAdvice,
    string ConfidenceLevel,
    string? Limitation);
