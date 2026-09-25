using System.Text;
using System.Text.Json;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class OperationRequestIdentityTests
{
    private static readonly Guid Nonce = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid PlanId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string PlanHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ExpectedRevision = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void canonical_apply_request_has_stable_host_neutral_bytes_and_hashes()
    {
        var claims = CreateClaims();
        var request = CreateRequest(epochClaims: claims);

        Encoding.UTF8.GetString(claims.CanonicalBytes.AsSpan()).ShouldBe(
            "{\"schemaVersion\":\"rt.operation-epoch-claims.v1\",\"repositoryId\":\"repository-001\",\"principalId\":\"principal-001\",\"epoch\":42,\"maximumOperationCount\":8,\"maximumRequestBytes\":1048576,\"notAfterUtc\":\"2030-01-02T03:04:05.0000000Z\"}");
        claims.ClaimsHash.ShouldBe(
            "fa3d80279ddb4a7e7e5cb73d2c375261508432151503b9eaca31b1a7f85160b0");
        Encoding.UTF8.GetString(request.CanonicalBytes.AsSpan()).ShouldBe(
            "{\"schemaVersion\":\"rt.apply-request.v1\",\"repositoryId\":\"repository-001\",\"operationId\":{\"epoch\":42,\"nonce\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"},\"planId\":\"11111111-2222-3333-4444-555555555555\",\"planHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"expectedRevision\":\"0123456789abcdef0123456789abcdef01234567\",\"operationEpochClaimsHash\":\"fa3d80279ddb4a7e7e5cb73d2c375261508432151503b9eaca31b1a7f85160b0\"}");
        request.Identity.ShouldBe(new OperationRequestIdentity(
            "3e13fcf811841798df15ed8975a8cdc4ff814c60253937567cc02442f7f1c5be",
            PlanId,
            PlanHash,
            ExpectedRevision,
            claims.ClaimsHash));
        request.Identity.PlanId.ShouldBe(PlanId);
        request.Identity.PlanHash.ShouldBe(PlanHash);
        request.Identity.ExpectedRevision.ShouldBe(ExpectedRevision);
        request.Identity.OperationEpochClaimsHash.ShouldBe(claims.ClaimsHash);
    }

    [Fact]
    public void every_semantic_apply_field_changes_the_canonical_request_hash()
    {
        var baseline = CreateRequest();
        var variants = new Dictionary<string, CanonicalApplyRequest>
        {
            ["repository"] = CreateRequest(repositoryId: "repository-002"),
            ["operation epoch"] = CreateRequest(epoch: 43),
            ["operation nonce"] = CreateRequest(nonce: Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff")),
            ["plan ID"] = CreateRequest(planId: Guid.Parse("22222222-3333-4444-5555-666666666666")),
            ["plan hash"] = CreateRequest(planHash: new string('b', 64)),
            ["expected revision"] = CreateRequest(expectedRevision: new string('f', 40)),
            ["submitting principal"] = CreateRequest(principalId: "principal-002"),
            ["epoch operation limit"] = CreateRequest(maximumOperationCount: 9),
            ["epoch byte limit"] = CreateRequest(maximumRequestBytes: 1048577),
            ["epoch expiry"] = CreateRequest(notAfterUtc: new DateTimeOffset(
                2030, 1, 2, 3, 4, 6, TimeSpan.Zero))
        };

        foreach (var (field, variant) in variants)
        {
            variant.Identity.RequestHash.ShouldNotBe(
                baseline.Identity.RequestHash,
                $"Changing the semantic {field} must change the canonical request hash.");
        }
    }

    [Fact]
    public void operation_identity_is_repository_scoped_and_not_an_authorization_secret()
    {
        var first = CreateRequest(repositoryId: "repository-001");
        var second = CreateRequest(repositoryId: "repository-002");

        first.OperationId.ShouldBe(second.OperationId);
        first.RepositoryId.ShouldNotBe(second.RepositoryId);
        first.Identity.RequestHash.ShouldNotBe(second.Identity.RequestHash);
    }

    [Fact]
    public void equivalent_epoch_expiry_offsets_have_identical_canonical_claims()
    {
        var utc = CreateClaims();
        var offset = CreateClaims(notAfterUtc: new DateTimeOffset(
            2030, 1, 2, 13, 4, 5, TimeSpan.FromHours(10)));

        offset.CanonicalBytes.ShouldBe(utc.CanonicalBytes);
        offset.ClaimsHash.ShouldBe(utc.ClaimsHash);
    }

    [Fact]
    public void request_construction_rejects_incomplete_or_mismatched_identity()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new OperationId(0, Nonce));
        Should.Throw<ArgumentException>(() => new OperationId(42, Guid.Empty));
        Should.Throw<ArgumentOutOfRangeException>(() => CreateClaims(epoch: 0));
        Should.Throw<ArgumentOutOfRangeException>(() => CreateClaims(maximumOperationCount: 0));
        Should.Throw<ArgumentOutOfRangeException>(() => CreateClaims(maximumRequestBytes: 0));
        Should.Throw<ArgumentException>(() => CreateRequest(planId: Guid.Empty));
        Should.Throw<ArgumentException>(() => CreateRequest(planHash: "not-a-hash"));
        Should.Throw<ArgumentException>(() => CreateRequest(expectedRevision: " "));
        Should.Throw<ArgumentException>(() => new OperationRequestIdentity(
            new string('a', 64),
            Guid.Empty,
            PlanHash,
            ExpectedRevision,
            new string('b', 64)));

        var claims = CreateClaims();
        Should.Throw<ArgumentNullException>(() => CanonicalApplyRequest.Create(
            "repository-001",
            null!,
            PlanId,
            PlanHash,
            ExpectedRevision,
            claims));
        Should.Throw<ArgumentNullException>(() => CanonicalApplyRequest.Create(
            "repository-001",
            new OperationId(42, Nonce),
            PlanId,
            PlanHash,
            ExpectedRevision,
            null!));

        var wrongRepository = CreateClaims(repositoryId: "repository-002");
        Should.Throw<ArgumentException>(() => CreateRequest(epochClaims: wrongRepository));
        var wrongEpoch = CreateClaims(epoch: 43);
        Should.Throw<ArgumentException>(() => CreateRequest(epochClaims: wrongEpoch));
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void request_identity_rejects_unmatched_utf16_surrogates(int unmatchedCodeUnit)
    {
        var malformed = $"value-{(char)unmatchedCodeUnit}";

        Should.Throw<ArgumentException>(() => CreateClaims(repositoryId: malformed));
        Should.Throw<ArgumentException>(() => CreateClaims(principalId: malformed));

        var claims = CreateClaims();
        Should.Throw<ArgumentException>(() => CanonicalApplyRequest.Create(
            "repository-001",
            new OperationId(42, Nonce),
            PlanId,
            PlanHash,
            malformed,
            claims));
        Should.Throw<ArgumentException>(() => new OperationRequestIdentity(
            new string('a', 64),
            PlanId,
            PlanHash,
            malformed,
            claims.ClaimsHash));
    }

    [Fact]
    public void canonical_identity_preserves_valid_supplementary_unicode()
    {
        const string supplementaryScalar = "\U0001F680";
        var repositoryId = $"repository-{supplementaryScalar}";
        var principalId = $"principal-{supplementaryScalar}";
        var expectedRevision = $"revision-{supplementaryScalar}";
        var claims = CreateClaims(repositoryId: repositoryId, principalId: principalId);
        var request = CreateRequest(
            repositoryId: repositoryId,
            expectedRevision: expectedRevision,
            epochClaims: claims);

        using var claimsJson = JsonDocument.Parse(
            Encoding.UTF8.GetString(claims.CanonicalBytes.AsSpan()));
        claimsJson.RootElement.GetProperty("repositoryId").GetString().ShouldBe(repositoryId);
        claimsJson.RootElement.GetProperty("principalId").GetString().ShouldBe(principalId);
        using var requestJson = JsonDocument.Parse(
            Encoding.UTF8.GetString(request.CanonicalBytes.AsSpan()));
        requestJson.RootElement.GetProperty("repositoryId").GetString().ShouldBe(repositoryId);
        requestJson.RootElement.GetProperty("expectedRevision").GetString().ShouldBe(expectedRevision);
        request.Identity.ExpectedRevision.ShouldBe(expectedRevision);
    }

    private static CanonicalApplyRequest CreateRequest(
        string repositoryId = "repository-001",
        string principalId = "principal-001",
        long epoch = 42,
        Guid? nonce = null,
        Guid? planId = null,
        string planHash = PlanHash,
        string expectedRevision = ExpectedRevision,
        int maximumOperationCount = 8,
        long maximumRequestBytes = 1024 * 1024,
        DateTimeOffset? notAfterUtc = null,
        OperationEpochClaims? epochClaims = null) => CanonicalApplyRequest.Create(
            repositoryId,
            new OperationId(epoch, nonce ?? Nonce),
            planId ?? PlanId,
            planHash,
            expectedRevision,
            epochClaims ?? CreateClaims(
                repositoryId,
                principalId,
                epoch,
                maximumOperationCount,
                maximumRequestBytes,
                notAfterUtc));

    private static OperationEpochClaims CreateClaims(
        string repositoryId = "repository-001",
        string principalId = "principal-001",
        long epoch = 42,
        int maximumOperationCount = 8,
        long maximumRequestBytes = 1024 * 1024,
        DateTimeOffset? notAfterUtc = null) => new(
            repositoryId,
            principalId,
            epoch,
            maximumOperationCount,
            maximumRequestBytes,
            notAfterUtc ?? new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
}
