using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using RoslynTools.Services.SemanticReview;
using Shouldly;
using Xunit.Abstractions;

namespace RoslynTools.Tests.Analysis;

public sealed class TypeSafeLiveSemanticReviewTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    [Trait("Category", "LiveTypeSafe")]
    public async Task explicitly_opted_in_synthetic_smoke_returns_a_valid_choice()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_TYPESAFE_LIVE_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")))
        {
            throw new InvalidOperationException(
                "RUN_TYPESAFE_LIVE_SMOKE=1 requires TYPESAFE_API_KEY.");
        }

        const string source = "public sealed class Handler { public void Run() => new Service().Execute(); }";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
        using var handler = new ResponseShapeHandler();
        using var client = new HttpClient(handler);
        using var provider = new TypeSafeSemanticReviewProvider(
            client,
            () => Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"),
            Task.Delay);

        var result = await provider.ReviewAsync(new SemanticReviewProviderRequest(
            "DIP",
            "Handler.Run",
            "fixtures/synthetic-smoke.cs",
            1,
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["detector"] = "raw_dip_inline_construction"
            },
            true,
            "synthetic-smoke-v1",
            hash,
            "jev-latest",
            SemanticReviewQuestionSets.SolidV1));
        var response = result.Match(
            value => value,
            failure => throw new InvalidOperationException(
                $"{failure.Kind}: {failure.Message} Response shape: {handler.SafeSummary}"));

        SemanticReviewClassifications.All.ShouldContain(response.Classification);
        _output.WriteLine(
            "classification={0}|confidence={1:0.000}|model={2}|request={3}",
            response.Classification,
            response.Confidence,
            response.ResolvedModel,
            response.RequestId);
    }

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

    private sealed class ResponseShapeHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(new HttpClientHandler());

        internal string SafeSummary { get; private set; } = "no response";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await _inner.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            SafeSummary = $"headers=[{string.Join(",", response.Headers.Select(header => header.Key))}]; "
                          + Summarize(body);
            var replacement = new ByteArrayContent(body);
            foreach (var header in response.Content.Headers)
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = replacement;
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        private static string Summarize(byte[] body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var rootNames = string.Join(",", root.EnumerateObject().Select(property => property.Name));
                if (!root.TryGetProperty("answers", out var answers)
                    || answers.ValueKind != JsonValueKind.Object)
                {
                    return $"root=[{rootNames}]; answers=missing";
                }

                var answerShapes = answers.EnumerateObject().Select(answer =>
                {
                    var value = answer.Value;
                    var properties = value.ValueKind == JsonValueKind.Object
                        ? string.Join(",", value.EnumerateObject().Select(property => property.Name))
                        : value.ValueKind.ToString();
                    var type = value.ValueKind == JsonValueKind.Object
                               && value.TryGetProperty("type", out var typeElement)
                        ? typeElement.GetString()
                        : null;
                    return $"{answer.Name}:type={type ?? "missing"};properties=[{properties}]";
                });
                return $"root=[{rootNames}]; answers=[{string.Join(";", answerShapes)}]";
            }
            catch (JsonException)
            {
                return "invalid JSON";
            }
        }
    }
}
