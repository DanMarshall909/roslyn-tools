using System.Net;
using System.Net.Http.Headers;
using System.Text;

using RoslynTools.Services.SemanticReview;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public sealed class TypeSafeSemanticReviewProviderTests
{
    [Fact]
    public async Task missing_credential_fails_before_http_and_never_exposes_a_secret()
    {
        var handler = new RecordingHandler(_ => SuccessResponse());
        using var client = new HttpClient(handler);
        using var provider = new TypeSafeSemanticReviewProvider(
            client,
            () => null,
            (_, _) => Task.CompletedTask);

        var result = await provider.ReviewAsync(CreateRequest());
        var failure = result.Match(
            _ => throw new InvalidOperationException("Expected failure."),
            value => value);

        failure.Kind.ShouldBe(SemanticReviewFailureKind.MissingCredential);
        handler.Requests.ShouldBeEmpty();
        failure.Message.ShouldNotContain("Bearer", Case.Insensitive);
    }

    [Fact]
    public async Task success_sends_structured_bounded_state_and_parses_choice_provenance()
    {
        var handler = new RecordingHandler(_ => SuccessResponse());
        using var client = new HttpClient(handler);
        using var provider = new TypeSafeSemanticReviewProvider(
            client,
            () => "test-api-key",
            (_, _) => Task.CompletedTask);

        var result = await provider.ReviewAsync(CreateRequest());
        var response = result.Match(
            value => value,
            failure => throw new InvalidOperationException(failure.Message));

        response.Classification.ShouldBe("likely_violation");
        response.ResolvedModel.ShouldBe("jev-1.13.0");
        response.RequestId.ShouldBe("request-123");
        response.Probabilities["uncertain"].ShouldBe(0.15);
        response.Confidence.ShouldBe(0.75);
        response.MeaningfulDesignProblem.ShouldBe(0.9);
        response.AcceptedException.ShouldBe(0.08);
        response.FalsePositiveReason.ShouldBe("no_accepted_exception");
        response.FalsePositiveReasonProbabilities["composition_root"].ShouldBe(0.05);
        handler.Requests.Count.ShouldBe(1);
        handler.Requests[0].Authorization.ShouldBe("Bearer test-api-key");
        handler.Requests[0].Body.ShouldContain("candidateSource", Case.Sensitive);
        handler.Requests[0].Body.ShouldContain("untrusted_csharp_data", Case.Sensitive);
        handler.Requests[0].Body.ShouldContain("solid-review-v2", Case.Sensitive);
        handler.Requests[0].Body.ShouldContain("meaningful_design_problem", Case.Sensitive);
        handler.Requests[0].Body.ShouldContain("accepted_exception", Case.Sensitive);
        handler.Requests[0].Body.ShouldContain("false_positive_reason", Case.Sensitive);
        handler.Requests[0].Body.ShouldNotContain("test-api-key", Case.Sensitive);
        handler.Requests[0].Body.ShouldNotContain("unrelated declaration", Case.Sensitive);
    }

    [Fact]
    public async Task success_reads_request_id_from_current_api_response_header()
    {
        var response = JsonResponse("""
            {
              "model": "jev-1.13.0",
              "answers": {
                "review_classification": {
                  "type": "choice",
                  "choice": "likely_violation",
                  "probabilities": {
                    "likely_violation": 0.8,
                    "uncertain": 0.15,
                    "likely_false_positive": 0.05
                  },
                  "confidence": 0.75
                },
                "meaningful_design_problem": {
                  "type": "noul",
                  "noul": 0.9
                },
                "accepted_exception": {
                  "type": "noul",
                  "noul": 0.08
                },
                "false_positive_reason": {
                  "type": "choice",
                  "choice": "no_accepted_exception",
                  "probabilities": {
                    "composition_root": 0.05,
                    "immutable_value": 0.01,
                    "framework_construction": 0.02,
                    "exception_construction": 0.02,
                    "no_accepted_exception": 0.9
                  },
                  "confidence": 0.84
                }
              }
            }
            """);
        response.Headers.Add("x-typesafe-request-id", "header-request-123");
        var handler = new RecordingHandler(_ => response);
        using var client = new HttpClient(handler);
        using var provider = new TypeSafeSemanticReviewProvider(
            client,
            () => "test-api-key",
            (_, _) => Task.CompletedTask);

        var result = await provider.ReviewAsync(CreateRequest());
        var parsed = result.Match(
            value => value,
            failure => throw new InvalidOperationException(failure.Message));

        parsed.RequestId.ShouldBe("header-request-123");
    }

    [Fact]
    public async Task unauthorized_response_maps_to_redacted_authentication_failure()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new HttpClient(handler);
        using var provider = new TypeSafeSemanticReviewProvider(
            client,
            () => "sensitive-key",
            (_, _) => Task.CompletedTask);

        var result = await provider.ReviewAsync(CreateRequest());
        var failure = result.Match(
            _ => throw new InvalidOperationException("Expected failure."),
            value => value);

        failure.Kind.ShouldBe(SemanticReviewFailureKind.Authentication);
        failure.StatusCode.ShouldBe(401);
        failure.Message.ShouldNotContain("sensitive-key", Case.Sensitive);
    }

    [Fact]
    public async Task rate_limit_retries_are_bounded_and_remain_explicit()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var client = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        using var provider = new TypeSafeSemanticReviewProvider(
            client,
            () => "test-api-key",
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await provider.ReviewAsync(CreateRequest());
        var failure = result.Match(
            _ => throw new InvalidOperationException("Expected failure."),
            value => value);

        failure.Kind.ShouldBe(SemanticReviewFailureKind.RateLimit);
        failure.Retryable.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(TypeSafeSemanticReviewProvider.MaximumAttempts);
        delays.Count.ShouldBe(TypeSafeSemanticReviewProvider.MaximumAttempts - 1);
    }

    [Fact]
    public async Task malformed_choice_response_is_not_presented_as_an_assessment()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {
              "model": "jev-1.13.0",
              "request_id": "request-123",
              "answers": {
                "review_classification": {
                  "type": "noul",
                  "noul": 0.9
                }
              }
            }
            """));
        using var client = new HttpClient(handler);
        using var provider = new TypeSafeSemanticReviewProvider(
            client,
            () => "test-api-key",
            (_, _) => Task.CompletedTask);

        var result = await provider.ReviewAsync(CreateRequest());
        var failure = result.Match(
            _ => throw new InvalidOperationException("Expected failure."),
            value => value);

        failure.Kind.ShouldBe(SemanticReviewFailureKind.MalformedResponse);
    }

    private static SemanticReviewProviderRequest CreateRequest() => new(
        "DIP",
        "Handler.Run",
        "src/Handler.cs",
        12,
        "public void Run() => new Service().Execute();",
        new Dictionary<string, string>
        {
            ["findingKind"] = "inline_construction",
            ["concreteType"] = "Service"
        },
        true,
        "revision-abc",
        "hash-abc",
        "jev-latest",
        "solid-review-v2");

    private static HttpResponseMessage SuccessResponse() => JsonResponse("""
        {
          "model": "jev-1.13.0",
          "request_id": "request-123",
          "answers": {
            "review_classification": {
              "type": "choice",
              "choice": "likely_violation",
              "probabilities": {
                "likely_violation": 0.8,
                "uncertain": 0.15,
                "likely_false_positive": 0.05
              },
              "confidence": 0.75
            },
            "meaningful_design_problem": {
              "type": "noul",
              "noul": 0.9
            },
            "accepted_exception": {
              "type": "noul",
              "noul": 0.08
            },
            "false_positive_reason": {
              "type": "choice",
              "choice": "no_accepted_exception",
              "probabilities": {
                "composition_root": 0.05,
                "immutable_value": 0.01,
                "framework_construction": 0.02,
                "exception_construction": 0.02,
                "no_accepted_exception": 0.9
              },
              "confidence": 0.84
            }
          },
          "usage": { "input_tokens": 300, "output_tokens": 20 }
        }
        """);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        internal List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(FormatAuthorization(request.Headers.Authorization), body));
            return responseFactory(request);
        }

        private static string? FormatAuthorization(AuthenticationHeaderValue? authorization) =>
            authorization is null ? null : $"{authorization.Scheme} {authorization.Parameter}";
    }

    private sealed record RecordedRequest(string? Authorization, string Body);
}
