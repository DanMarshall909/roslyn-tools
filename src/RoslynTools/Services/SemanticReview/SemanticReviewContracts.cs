using System.Text.Json.Serialization;

using RoslynTools.Core;

namespace RoslynTools.Services.SemanticReview;

internal static class SemanticReviewQuestionSets
{
    internal const string SolidV2 = "solid-review-v2";
}

internal static class SemanticReviewClassifications
{
    internal const string LikelyViolation = "likely_violation";
    internal const string Uncertain = "uncertain";
    internal const string LikelyFalsePositive = "likely_false_positive";

    internal static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [LikelyViolation, Uncertain, LikelyFalsePositive],
        StringComparer.Ordinal);
}

internal static class SemanticReviewFalsePositiveReasons
{
    internal const string NoAcceptedException = "no_accepted_exception";

    private static readonly Dictionary<string, HashSet<string>> ReasonsByPrinciple =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SRP"] = Set("cohesive_workflow", "application_orchestration", "boundary_adapter", "generated_or_framework_code"),
            ["ISP"] = Set("cohesive_protocol", "framework_contract", "marker_or_capability_contract", "shared_consumer_usage"),
            ["DIP"] = Set("composition_root", "immutable_value", "framework_construction", "exception_construction"),
            ["OCP"] = Set("closed_set_classification", "boundary_dispatch", "serializer_or_protocol_mapping", "generated_or_framework_code"),
            ["LSP"] = Set("explicitly_constrained_adapter", "platform_guard", "optional_framework_member", "base_contract_permits_behavior")
        };

    internal static IReadOnlySet<string> ForPrinciple(string principle) =>
        ReasonsByPrinciple.TryGetValue(principle, out var reasons)
            ? reasons
            : Set();

    private static HashSet<string> Set(params string[] reasons) => new(
        [.. reasons, NoAcceptedException],
        StringComparer.Ordinal);
}

internal sealed record SolidReviewRequest(
    string? Solution = null,
    string Principle = "all",
    int MinMethods = 10,
    int MinDeps = 5,
    int MinMembers = 7,
    int MinBranches = 2,
    string Provider = "typesafe",
    string Model = "jev-latest",
    int MaxCandidates = 20,
    bool DryRun = false,
    bool TransmitSource = false);

internal sealed record SemanticReviewProviderRequest(
    string Principle,
    string Symbol,
    string FilePath,
    int Line,
    string SanitizedSource,
    IReadOnlyDictionary<string, string> RawEvidence,
    bool EvidenceComplete,
    string RepositoryRevision,
    string SourceEvidenceHash,
    string RequestedModel,
    string QuestionSetVersion,
    string? ReviewQuestion = null,
    string? RuleDescription = null,
    IReadOnlyDictionary<string, string>? AcceptedExceptions = null,
    IReadOnlyList<string>? EvidenceGuidance = null);

internal sealed record SemanticReviewProviderResponse(
    string Classification,
    IReadOnlyDictionary<string, double> Probabilities,
    double Confidence,
    double MeaningfulDesignProblem,
    double AcceptedException,
    string FalsePositiveReason,
    IReadOnlyDictionary<string, double> FalsePositiveReasonProbabilities,
    double FalsePositiveReasonConfidence,
    string Provider,
    string RequestedModel,
    string ResolvedModel,
    string QuestionSetVersion,
    string RequestId);

internal enum SemanticReviewFailureKind
{
    MissingCredential,
    Authentication,
    RateLimit,
    Overloaded,
    Network,
    ProviderValidation,
    MalformedResponse,
    UnknownProvider
}

internal sealed record SemanticReviewFailure(
    SemanticReviewFailureKind Kind,
    string Message,
    bool Retryable = false,
    int? StatusCode = null);

internal interface ISemanticReviewProvider
{
    string Name { get; }

    Task<Result<SemanticReviewProviderResponse, SemanticReviewFailure>> ReviewAsync(
        SemanticReviewProviderRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed record SolidReviewPlanItem(
    [property: JsonPropertyName("candidateId")] string CandidateId,
    [property: JsonPropertyName("principle")] string Principle,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("sourceBytes")] int SourceBytes,
    [property: JsonPropertyName("sourceEvidenceHash")] string SourceEvidenceHash,
    [property: JsonPropertyName("evidenceComplete")] bool EvidenceComplete,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("rawEvidence")] IReadOnlyDictionary<string, string> RawEvidence);

internal sealed record SolidReviewAssessment(
    [property: JsonPropertyName("candidateId")] string CandidateId,
    [property: JsonPropertyName("principle")] string Principle,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("classification")] string Classification,
    [property: JsonPropertyName("providerClassification")] string ProviderClassification,
    [property: JsonPropertyName("advisory")] string Advisory,
    [property: JsonPropertyName("probabilities")] IReadOnlyDictionary<string, double> Probabilities,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("meaningfulDesignProblem")] double MeaningfulDesignProblem,
    [property: JsonPropertyName("acceptedException")] double AcceptedException,
    [property: JsonPropertyName("providerFalsePositiveReason")] string ProviderFalsePositiveReason,
    [property: JsonPropertyName("falsePositiveReasonProbabilities")] IReadOnlyDictionary<string, double> FalsePositiveReasonProbabilities,
    [property: JsonPropertyName("falsePositiveReasonConfidence")] double FalsePositiveReasonConfidence,
    [property: JsonPropertyName("policyReasons")] IReadOnlyList<string> PolicyReasons,
    [property: JsonPropertyName("evidenceComplete")] bool EvidenceComplete,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("requestedModel")] string RequestedModel,
    [property: JsonPropertyName("resolvedModel")] string ResolvedModel,
    [property: JsonPropertyName("questionSetVersion")] string QuestionSetVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("repositoryRevision")] string RepositoryRevision,
    [property: JsonPropertyName("sourceEvidenceHash")] string SourceEvidenceHash,
    [property: JsonPropertyName("rawEvidence")] IReadOnlyDictionary<string, string> RawEvidence);

internal sealed record SolidReviewFailureItem(
    [property: JsonPropertyName("candidateId")] string? CandidateId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable,
    [property: JsonPropertyName("statusCode")] int? StatusCode);

internal sealed record SolidReviewResult(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("dryRun")] bool DryRun,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("requestedModel")] string RequestedModel,
    [property: JsonPropertyName("questionSetVersion")] string QuestionSetVersion,
    [property: JsonPropertyName("repositoryRevision")] string RepositoryRevision,
    [property: JsonPropertyName("estimatedRequestCount")] int EstimatedRequestCount,
    [property: JsonPropertyName("raw")] SolidResult Raw,
    [property: JsonPropertyName("plan")] IReadOnlyList<SolidReviewPlanItem> Plan,
    [property: JsonPropertyName("assessments")] IReadOnlyList<SolidReviewAssessment> Assessments,
    [property: JsonPropertyName("failures")] IReadOnlyList<SolidReviewFailureItem> Failures);

internal static class SolidReviewPolicy
{
    internal const double MinimumAutomaticProbability = 0.60;
    internal const double MaximumContradictoryProbability = 0.40;

    internal static string ToAdvisory(string classification) => classification switch
    {
        SemanticReviewClassifications.LikelyViolation => "likely violation",
        SemanticReviewClassifications.LikelyFalsePositive => "likely false positive",
        _ => "uncertain"
    };

    internal static SolidReviewDecision Evaluate(
        string providerClassification,
        double classificationProbability,
        bool evidenceComplete,
        double meaningfulDesignProblem,
        double acceptedException,
        string falsePositiveReason,
        double falsePositiveReasonProbability,
        double minimumAutomaticProbability = MinimumAutomaticProbability,
        double maximumContradictoryProbability = MaximumContradictoryProbability)
    {
        if (!SemanticReviewClassifications.All.Contains(providerClassification))
            return Uncertain("invalid_provider_classification");

        if (!evidenceComplete)
            return Uncertain("evidence_incomplete");

        if (classificationProbability < minimumAutomaticProbability)
            return Uncertain("classification_probability_below_threshold");

        if (providerClassification == SemanticReviewClassifications.Uncertain)
            return Uncertain("provider_uncertain");

        if (providerClassification == SemanticReviewClassifications.LikelyViolation)
        {
            if (acceptedException >= minimumAutomaticProbability)
                return Uncertain("accepted_exception_conflicts_with_violation");

            if (meaningfulDesignProblem < minimumAutomaticProbability)
                return Uncertain("meaningful_design_problem_not_supported");

            if (acceptedException > maximumContradictoryProbability)
                return Uncertain("accepted_exception_remains_plausible");

            return new SolidReviewDecision(
                SemanticReviewClassifications.LikelyViolation,
                null,
                ["meaningful_design_problem_supported"]);
        }

        if (acceptedException < minimumAutomaticProbability)
            return Uncertain("accepted_exception_not_supported");

        if (meaningfulDesignProblem > maximumContradictoryProbability)
            return Uncertain("meaningful_design_problem_conflicts_with_exception");

        if (falsePositiveReason == SemanticReviewFalsePositiveReasons.NoAcceptedException
            || falsePositiveReasonProbability < minimumAutomaticProbability)
        {
            return Uncertain("false_positive_reason_not_supported");
        }

        return new SolidReviewDecision(
            SemanticReviewClassifications.LikelyFalsePositive,
            falsePositiveReason,
            ["accepted_exception_supported", $"accepted_exception:{falsePositiveReason}"]);
    }

    private static SolidReviewDecision Uncertain(string reason) => new(
        SemanticReviewClassifications.Uncertain,
        null,
        [reason]);
}

internal sealed record SolidReviewDecision(
    string Classification,
    string? FalsePositiveReason,
    IReadOnlyList<string> Reasons);
