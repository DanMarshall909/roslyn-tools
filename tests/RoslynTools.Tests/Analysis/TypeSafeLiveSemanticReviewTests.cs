using System.Security.Cryptography;
using System.Text;

using RoslynTools.Services.SemanticReview;
using Xunit.Abstractions;

namespace RoslynTools.Tests.Analysis;

public sealed class TypeSafeLiveSemanticReviewTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    [Trait("Category", "LiveTypeSafe")]
    public async Task explicitly_opted_in_fixture_evaluation_returns_valid_advisory_choices()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_TYPESAFE_LIVE_EVAL"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")))
        {
            throw new InvalidOperationException(
                "RUN_TYPESAFE_LIVE_EVAL=1 requires TYPESAFE_API_KEY.");
        }

        using var provider = new TypeSafeSemanticReviewProvider();
        var classifications = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < SolidSemanticReviewFixtures.All.Count; index++)
        {
            var fixture = SolidSemanticReviewFixtures.All[index];
            var sanitized = CandidateSourceSanitizer.Sanitize(fixture.Source);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sanitized)))
                .ToLowerInvariant();
            var request = new SemanticReviewProviderRequest(
                fixture.Principle,
                $"Fixture{index + 1}",
                $"fixtures/candidate-{index + 1:00}.cs",
                1,
                sanitized,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["detector"] = $"raw_{fixture.Principle.ToLowerInvariant()}_candidate"
                },
                fixture.EvidenceComplete,
                "fixture-corpus-v1",
                hash,
                "jev-latest",
                SemanticReviewQuestionSets.SolidV1);

            var result = await provider.ReviewAsync(request);
            var response = result.Match(
                value => value,
                failure => throw new InvalidOperationException(
                    $"{fixture.Name}: {failure.Kind}: {failure.Message}"));
            var displayed = SolidReviewPolicy.ApplyEvidenceGuard(
                fixture.Principle,
                response.Classification,
                fixture.EvidenceComplete);
            classifications[displayed] = classifications.GetValueOrDefault(displayed) + 1;
            _output.WriteLine(
                "{0}|expected={1}|actual={2}|confidence={3:0.000}|model={4}|request={5}",
                fixture.Name,
                fixture.ExpectedClassification,
                displayed,
                response.Confidence,
                response.ResolvedModel,
                response.RequestId);
        }

        _output.WriteLine(
            "summary|likely_violation={0}|uncertain={1}|likely_false_positive={2}",
            classifications.GetValueOrDefault(SemanticReviewClassifications.LikelyViolation),
            classifications.GetValueOrDefault(SemanticReviewClassifications.Uncertain),
            classifications.GetValueOrDefault(SemanticReviewClassifications.LikelyFalsePositive));
    }
}
