using System.Text.Json.Serialization;

using RoslynTools.Core;

namespace RoslynTools.Services.SemanticReview;

internal static class SemanticReviewQuestionSets
{
    internal const string SolidV1 = "solid-review-v1";
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
    string QuestionSetVersion);

internal sealed record SemanticReviewProviderResponse(
    string Classification,
    IReadOnlyDictionary<string, double> Probabilities,
    double Confidence,
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
    [property: JsonPropertyName("advisory")] string Advisory,
    [property: JsonPropertyName("probabilities")] IReadOnlyDictionary<string, double> Probabilities,
    [property: JsonPropertyName("confidence")] double Confidence,
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
    internal static string ApplyEvidenceGuard(
        string principle,
        string providerClassification,
        bool evidenceComplete)
    {
        if (!SemanticReviewClassifications.All.Contains(providerClassification))
            return SemanticReviewClassifications.Uncertain;

        if (string.Equals(principle, "ISP", StringComparison.OrdinalIgnoreCase)
            && !evidenceComplete
            && providerClassification == SemanticReviewClassifications.LikelyViolation)
        {
            return SemanticReviewClassifications.Uncertain;
        }

        return providerClassification;
    }

    internal static string ToAdvisory(string classification) => classification switch
    {
        SemanticReviewClassifications.LikelyViolation => "likely violation",
        SemanticReviewClassifications.LikelyFalsePositive => "likely false positive",
        _ => "uncertain"
    };
}
