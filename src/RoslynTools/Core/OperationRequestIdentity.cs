using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RoslynTools.Core;

file static class OperationRequestSchema
{
    internal const string EpochClaimsVersion = "rt.operation-epoch-claims.v1";
    internal const string ApplyRequestVersion = "rt.apply-request.v1";
}

file static class OperationRequestText
{
    private static readonly Encoding StrictUtf8 = Encoding.GetEncoding(
        Encoding.UTF8.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    internal static string Required(string value, string parameterName)
    {
        value = ChangePlanValidation.Required(value, parameterName);
        try
        {
            _ = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            // Stryker disable once String : Validation wording is diagnostic; the typed boundary is asserted.
            throw new ArgumentException("Value must contain well-formed UTF-16.", parameterName, exception);
        }
        return value;
    }
}

/// <summary>
///     Caller-known operation address constructed before dispatch. The repository ID is
///     deliberately separate because the same epoch/nonce value may be used in two repositories.
/// </summary>
internal sealed record OperationId
{
    internal OperationId(long epoch, Guid nonce)
    {
        if (epoch <= 0)
            // Stryker disable once String : Validation wording is diagnostic; the typed boundary is asserted.
            throw new ArgumentOutOfRangeException(nameof(epoch), "An operation epoch must be positive.");
        if (nonce == Guid.Empty)
            // Stryker disable once String : Validation wording is diagnostic; the typed boundary is asserted.
            throw new ArgumentException("An operation nonce cannot be empty.", nameof(nonce));

        Epoch = epoch;
        Nonce = nonce;
    }

    internal long Epoch { get; }

    internal Guid Nonce { get; }
}

/// <summary>
///     Canonical public claims from an authenticated operation-epoch token. Signature,
///     bearer proof, and other authentication material never enter this value.
/// </summary>
internal sealed class OperationEpochClaims
{
    private readonly ImmutableArray<byte> _canonicalBytes;

    internal OperationEpochClaims(
        string repositoryId,
        string principalId,
        long epoch,
        int maximumOperationCount,
        long maximumRequestBytes,
        DateTimeOffset notAfterUtc)
    {
        RepositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        PrincipalId = OperationRequestText.Required(principalId, nameof(principalId));
        if (epoch <= 0)
            // Stryker disable once String : Validation wording is diagnostic; the typed boundary is asserted.
            throw new ArgumentOutOfRangeException(nameof(epoch), "An operation epoch must be positive.");
        if (maximumOperationCount <= 0)
        {
            // Stryker disable once String : Validation wording is diagnostic; the typed boundary is asserted.
            throw new ArgumentOutOfRangeException(
                nameof(maximumOperationCount),
                "An operation epoch must admit a positive operation count.");
        }
        if (maximumRequestBytes <= 0)
        {
            // Stryker disable once String : Validation wording is diagnostic; the typed boundary is asserted.
            throw new ArgumentOutOfRangeException(
                nameof(maximumRequestBytes),
                "An operation epoch must admit a positive request-byte count.");
        }

        Epoch = epoch;
        MaximumOperationCount = maximumOperationCount;
        MaximumRequestBytes = maximumRequestBytes;
        NotAfterUtc = notAfterUtc.ToUniversalTime();
        _canonicalBytes = ImmutableArray.CreateRange(
            CanonicalOperationRequest.SerializeEpochClaims(this));
        ClaimsHash = CanonicalOperationRequest.ComputeHash(_canonicalBytes.AsSpan());
    }

    internal string RepositoryId { get; }

    internal string PrincipalId { get; }

    internal long Epoch { get; }

    internal int MaximumOperationCount { get; }

    internal long MaximumRequestBytes { get; }

    internal DateTimeOffset NotAfterUtc { get; }

    internal ImmutableArray<byte> CanonicalBytes => _canonicalBytes;

    internal string ClaimsHash { get; }
}

internal sealed record OperationRequestIdentity
{
    internal OperationRequestIdentity(
        string requestHash,
        Guid planId,
        string planHash,
        string expectedRevision,
        string operationEpochClaimsHash)
    {
        RequestHash = ChangePlanValidation.NormalizeSha256(requestHash, nameof(requestHash));
        if (planId == Guid.Empty)
            // Stryker disable once String : Validation wording is diagnostic; the typed boundary is asserted.
            throw new ArgumentException("A plan ID cannot be empty.", nameof(planId));
        PlanId = planId;
        PlanHash = ChangePlanValidation.NormalizeSha256(planHash, nameof(planHash));
        ExpectedRevision = OperationRequestText.Required(expectedRevision, nameof(expectedRevision));
        OperationEpochClaimsHash = ChangePlanValidation.NormalizeSha256(
            operationEpochClaimsHash,
            nameof(operationEpochClaimsHash));
    }

    internal string RequestHash { get; }

    internal Guid PlanId { get; }

    internal string PlanHash { get; }

    internal string ExpectedRevision { get; }

    internal string OperationEpochClaimsHash { get; }
}

/// <summary>
///     Exact host-neutral semantic apply request. Transport attempts, replay authority,
///     cancellation, authentication proof, and this request's own hash are absent by design.
/// </summary>
internal sealed class CanonicalApplyRequest
{
    private readonly ImmutableArray<byte> _canonicalBytes;

    private CanonicalApplyRequest(
        string repositoryId,
        OperationId operationId,
        Guid planId,
        string planHash,
        string expectedRevision,
        string operationEpochClaimsHash)
    {
        RepositoryId = repositoryId;
        OperationId = operationId;
        _canonicalBytes = ImmutableArray.CreateRange(CanonicalOperationRequest.SerializeApplyRequest(
            repositoryId,
            operationId,
            planId,
            planHash,
            expectedRevision,
            operationEpochClaimsHash));
        Identity = new OperationRequestIdentity(
            CanonicalOperationRequest.ComputeHash(_canonicalBytes.AsSpan()),
            planId,
            planHash,
            expectedRevision,
            operationEpochClaimsHash);
    }

    internal string RepositoryId { get; }

    internal OperationId OperationId { get; }

    internal OperationRequestIdentity Identity { get; }

    internal ImmutableArray<byte> CanonicalBytes => _canonicalBytes;

    internal static CanonicalApplyRequest Create(
        string repositoryId,
        OperationId operationId,
        Guid planId,
        string planHash,
        string expectedRevision,
        OperationEpochClaims epochClaims)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(epochClaims);
        planHash = ChangePlanValidation.NormalizeSha256(planHash, nameof(planHash));
        expectedRevision = OperationRequestText.Required(expectedRevision, nameof(expectedRevision));
        if (!string.Equals(repositoryId, epochClaims.RepositoryId, StringComparison.Ordinal))
        {
            // Stryker disable once String : Validation wording is diagnostic; the typed boundary is asserted.
            throw new ArgumentException(
                "The operation-epoch claims belong to a different repository.",
                nameof(epochClaims));
        }
        if (operationId.Epoch != epochClaims.Epoch)
        {
            // Stryker disable once String : Validation wording is diagnostic; the typed boundary is asserted.
            throw new ArgumentException(
                "The operation ID does not use the authenticated operation epoch.",
                nameof(operationId));
        }

        return new CanonicalApplyRequest(
            repositoryId,
            operationId,
            planId,
            planHash,
            expectedRevision,
            epochClaims.ClaimsHash);
    }
}

/// <summary>
///     Sole byte projection for operation-epoch public claims and semantic apply requests.
/// </summary>
file static class CanonicalOperationRequest
{
    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    internal static byte[] SerializeEpochClaims(OperationEpochClaims claims)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", OperationRequestSchema.EpochClaimsVersion);
            writer.WriteString("repositoryId", claims.RepositoryId);
            writer.WriteString("principalId", claims.PrincipalId);
            writer.WriteNumber("epoch", claims.Epoch);
            writer.WriteNumber("maximumOperationCount", claims.MaximumOperationCount);
            writer.WriteNumber("maximumRequestBytes", claims.MaximumRequestBytes);
            writer.WriteString(
                "notAfterUtc",
                claims.NotAfterUtc.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    internal static byte[] SerializeApplyRequest(
        string repositoryId,
        OperationId operationId,
        Guid planId,
        string planHash,
        string expectedRevision,
        string operationEpochClaimsHash)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", OperationRequestSchema.ApplyRequestVersion);
            writer.WriteString("repositoryId", repositoryId);
            writer.WritePropertyName("operationId");
            writer.WriteStartObject();
            writer.WriteNumber("epoch", operationId.Epoch);
            writer.WriteString("nonce", operationId.Nonce);
            writer.WriteEndObject();
            writer.WriteString("planId", planId);
            writer.WriteString("planHash", planHash);
            writer.WriteString("expectedRevision", expectedRevision);
            writer.WriteString("operationEpochClaimsHash", operationEpochClaimsHash);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    internal static string ComputeHash(ReadOnlySpan<byte> canonicalBytes) =>
        Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
}
