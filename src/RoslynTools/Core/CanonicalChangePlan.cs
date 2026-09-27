using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace RoslynTools.Core;

/// <summary>
///     The sole byte projection for <c>rt.change-plan.v1</c>. The explicit property order,
///     explicit nulls, lower-case enum tokens, UTC timestamp format, and UTF-8 JSON bytes
///     are part of the schema and shared by every host adapter.
/// </summary>
internal static class CanonicalChangePlan
{
    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    internal static byte[] Serialize(ChangePlanPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", payload.SchemaVersion);
            writer.WriteString("planId", payload.PlanId.ToString());
            WriteRepository(writer, payload.Repository);
            writer.WriteString("createdAtUtc", FormatUtc(payload.CreatedAtUtc));
            writer.WriteString("expiresAtUtc", FormatUtc(payload.ExpiresAtUtc));
            WriteOperations(writer, payload.Operations);
            WriteDependencies(writer, payload.Dependencies);
            WriteEffects(writer, payload.Effects);
            WriteStringArray(writer, "affectedPaths", payload.AffectedPaths);
            writer.WriteString("diff", payload.Diff);
            WriteDiagnostics(writer, payload.Diagnostics);
            WriteStringArray(writer, "assumptions", payload.Assumptions);
            WriteConfidence(writer, payload.Confidence);
            WriteStringArray(writer, "limitations", payload.Limitations);
            WriteWorkspace(writer, payload.Workspace);
            WriteVerification(writer, payload.VerificationActions);
            WriteCoordination(writer, payload.Coordination);
            WritePathPolicy(writer, payload.PathPolicy);
            writer.WriteString("cleanupPolicy", "retainUntilVerifiedTerminal");
            writer.WriteString("recoveryPolicy", "requireDurableJournal");
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    internal static string ComputeHash(ChangePlanPayload payload) =>
        Convert.ToHexString(SHA256.HashData(Serialize(payload))).ToLowerInvariant();

    internal static string ComputeEffectHash(ChangePlanEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteEffects(writer, [effect]);
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    internal static bool VerifyHash(ChangePlanPayload payload, string claimedHash)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!TryDecodeSha256(claimedHash, out var claimed))
            return false;

        var actual = SHA256.HashData(Serialize(payload));
        return CryptographicOperations.FixedTimeEquals(actual, claimed);
    }

    private static void WriteRepository(Utf8JsonWriter writer, ChangePlanRepository repository)
    {
        writer.WritePropertyName("repository");
        writer.WriteStartObject();
        writer.WriteString("id", repository.RepositoryId);
        writer.WriteString("root", repository.RepositoryRoot);
        writer.WriteString("resolvedRoot", repository.ResolvedRepositoryRoot);
        writer.WriteString("rootPlatformIdentity", repository.RootPlatformIdentity);
        writer.WriteString("baseRevision", repository.BaseRevision);
        writer.WriteString("sourceFingerprint", repository.SourceFingerprint);
        writer.WriteEndObject();
    }

    private static void WriteDependencies(
        Utf8JsonWriter writer,
        IReadOnlyList<ChangePlanDependency> dependencies)
    {
        writer.WritePropertyName("dependencies");
        writer.WriteStartArray();
        foreach (var dependency in dependencies)
        {
            writer.WriteStartObject();
            writer.WriteString("path", dependency.Path);
            writer.WriteString("resolvedPath", dependency.ResolvedPath);
            writer.WriteString("kind", WireName(dependency.Kind));
            WritePathState(writer, "expectedState", dependency.ExpectedState);
            WriteGuard(writer, dependency.Guard);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteOperations(
        Utf8JsonWriter writer,
        IReadOnlyList<ChangePlanOperation> operations)
    {
        writer.WritePropertyName("operations");
        writer.WriteStartArray();
        foreach (var operation in operations)
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", operation.Sequence);
            writer.WriteString("name", operation.Name);
            writer.WriteString("stableInput", operation.StableInput);
            WriteValues(writer, "arguments", operation.Arguments);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteEffects(
        Utf8JsonWriter writer,
        IReadOnlyList<ChangePlanEffect> effects)
    {
        writer.WritePropertyName("effects");
        writer.WriteStartArray();
        foreach (var effect in effects)
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", effect.Sequence);
            writer.WriteString("kind", WireName(effect.Kind));
            writer.WriteString("artifactKind", WireName(effect.ArtifactKind));
            writer.WriteString("sourcePath", effect.SourcePath);
            writer.WriteString("sourceResolvedPath", effect.SourceResolvedPath);
            WriteNullableString(writer, "destinationPath", effect.DestinationPath);
            WriteNullableString(writer, "destinationResolvedPath", effect.DestinationResolvedPath);
            WritePathState(writer, "sourceBefore", effect.SourceBefore);
            WritePathState(writer, "sourceAfter", effect.SourceAfter);
            WriteNullablePathState(writer, "destinationBefore", effect.DestinationBefore);
            WriteNullablePathState(writer, "destinationAfter", effect.DestinationAfter);
            WriteNullableString(
                writer,
                "originalContentBase64",
                effect.HasOriginalContent
                    ? Convert.ToBase64String(effect.OriginalContent.AsSpan())
                    : null);
            WriteNullableString(
                writer,
                "replacementContentBase64",
                effect.HasReplacementContent
                    ? Convert.ToBase64String(effect.ReplacementContent.AsSpan())
                    : null);
            WriteGuards(writer, effect.Guards);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteNullablePathState(
        Utf8JsonWriter writer,
        string propertyName,
        ChangePlanPathState? state)
    {
        if (state is null)
        {
            writer.WriteNull(propertyName);
            return;
        }
        WritePathState(writer, propertyName, state);
    }

    private static void WritePathState(
        Utf8JsonWriter writer,
        string propertyName,
        ChangePlanPathState state)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteBoolean("exists", state.Exists);
        writer.WriteString("kind", WireName(state.Kind));
        WriteNullableString(writer, "contentHash", state.ContentHash);
        WriteNullableString(writer, "platformFileIdentity", state.PlatformFileIdentity);
        WriteNullableNumber(writer, "linkCount", state.LinkCount);
        WriteStringArray(writer, "knownHardLinkAliases", state.KnownHardLinkAliases);
        WriteNullableString(writer, "encodingName", state.EncodingName);
        WriteNullableBoolean(writer, "hasByteOrderMark", state.HasByteOrderMark);
        WriteNullableString(writer, "newline", state.Newline);
        WriteNullableString(writer, "fileMode", state.FileMode);
        WriteNullableBoolean(writer, "executable", state.Executable);
        WriteNullableBoolean(writer, "readOnly", state.ReadOnly);
        WriteNullableString(writer, "symlinkTarget", state.SymlinkTarget);
        writer.WriteEndObject();
    }

    private static void WriteGuards(
        Utf8JsonWriter writer,
        IReadOnlyList<ChangePlanGuardRequirement> guards)
    {
        writer.WritePropertyName("guards");
        writer.WriteStartArray();
        foreach (var guard in guards)
        {
            writer.WriteStartObject();
            writer.WriteString("path", guard.Path);
            writer.WriteString("kind", WireName(guard.Kind));
            writer.WriteBoolean("required", true);
            WriteNullableString(writer, "expectedToken", guard.ExpectedToken);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteGuard(Utf8JsonWriter writer, ChangePlanGuardRequirement guard)
    {
        writer.WritePropertyName("guard");
        writer.WriteStartObject();
        writer.WriteString("path", guard.Path);
        writer.WriteString("kind", WireName(guard.Kind));
        writer.WriteBoolean("required", true);
        writer.WriteString("expectedToken", guard.ExpectedToken);
        writer.WriteEndObject();
    }

    private static void WriteDiagnostics(
        Utf8JsonWriter writer,
        IReadOnlyList<ChangePlanDiagnostic> diagnostics)
    {
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", WireName(diagnostic.Severity));
            writer.WriteString("message", diagnostic.Message);
            WriteValues(writer, "context", diagnostic.Context);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteConfidence(Utf8JsonWriter writer, ChangePlanConfidence confidence)
    {
        writer.WritePropertyName("confidence");
        writer.WriteStartObject();
        writer.WriteString("level", confidence.Level);
        writer.WriteString("basis", confidence.Basis);
        WriteStringArray(writer, "evidence", confidence.Evidence);
        writer.WriteEndObject();
    }

    private static void WriteWorkspace(Utf8JsonWriter writer, ChangePlanWorkspace workspace)
    {
        writer.WritePropertyName("workspace");
        writer.WriteStartObject();
        writer.WriteString("state", WireName(workspace.State));
        writer.WriteString("solutionPath", workspace.SolutionPath);
        writer.WriteString("configuration", workspace.Configuration);
        WriteStringArray(writer, "targetFrameworks", workspace.TargetFrameworks);
        writer.WriteString("sdkIdentity", workspace.SdkIdentity);
        writer.WriteString("compilerIdentity", workspace.CompilerIdentity);
        writer.WriteString("evaluatorMode", workspace.EvaluatorMode);
        writer.WriteString("trustState", workspace.TrustState);
        writer.WriteEndObject();
    }

    private static void WriteVerification(
        Utf8JsonWriter writer,
        IReadOnlyList<ChangePlanVerificationAction> actions)
    {
        writer.WritePropertyName("verificationActions");
        writer.WriteStartArray();
        foreach (var action in actions)
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", action.Sequence);
            writer.WriteString("name", action.Name);
            writer.WriteString("capability", action.Capability);
            writer.WriteString("expectedOutcome", action.ExpectedOutcome);
            writer.WriteBoolean("required", action.Required);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteCoordination(Utf8JsonWriter writer, ChangePlanCoordination coordination)
    {
        writer.WritePropertyName("coordination");
        writer.WriteStartObject();
        writer.WriteString("domain", WireName(coordination.Domain));
        writer.WriteString("mode", WireName(coordination.Mode));
        writer.WriteString("writerSetProof", coordination.WriterSetProof);
        writer.WriteString("controlRoot", coordination.ControlRoot);
        writer.WriteString("controlRootResolvedPath", coordination.ControlRootResolvedPath);
        writer.WriteString("controlRootPlatformIdentity", coordination.ControlRootPlatformIdentity);
        writer.WriteString("filesystemVolumeIdentity", coordination.FilesystemVolumeIdentity);
        writer.WriteString("stagingRoot", coordination.StagingRoot);
        writer.WriteString("stagingRootResolvedPath", coordination.StagingRootResolvedPath);
        writer.WriteString("stagingRootPlatformIdentity", coordination.StagingRootPlatformIdentity);
        writer.WriteString("backupRoot", coordination.BackupRoot);
        writer.WriteString("backupRootResolvedPath", coordination.BackupRootResolvedPath);
        writer.WriteString("backupRootPlatformIdentity", coordination.BackupRootPlatformIdentity);
        writer.WriteEndObject();
    }

    private static void WritePathPolicy(Utf8JsonWriter writer, ChangePlanPathPolicy policy)
    {
        writer.WritePropertyName("pathPolicy");
        writer.WriteStartObject();
        writer.WriteString("caseSensitivity", WireName(policy.CaseSensitivity));
        writer.WriteString("longPath", "reject");
        writer.WriteString("readOnly", "reject");
        writer.WriteString("symlink", "reject");
        writer.WriteString("hardLink", "reject");
        writer.WriteEndObject();
    }

    private static void WriteValues(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<ChangePlanValue> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStartObject();
            writer.WriteString("name", value.Name);
            WriteNullableString(writer, "value", value.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<string> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteString(propertyName, value);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, int? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteNumber(propertyName, value.Value);
    }

    private static void WriteNullableBoolean(Utf8JsonWriter writer, string propertyName, bool? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteBoolean(propertyName, value.Value);
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture);

    private static bool TryDecodeSha256(string? value, out byte[] bytes)
    {
        bytes = new byte[32];
        if (value is null || value.Length != bytes.Length * 2)
            return false;

        for (var index = 0; index < bytes.Length; index++)
        {
            var high = HexValue(value[index * 2]);
            var low = HexValue(value[index * 2 + 1]);
            if (high < 0 || low < 0)
                return false;
            bytes[index] = (byte)((high << 4) | low);
        }
        return true;
    }

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        _ => -1
    };

    private static string WireName(ChangePlanEffectKind value) => value switch
    {
        ChangePlanEffectKind.Create => "create",
        ChangePlanEffectKind.Replace => "replace",
        ChangePlanEffectKind.Delete => "delete",
        ChangePlanEffectKind.Move => "move",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string WireName(ChangePlanArtifactKind value) => value switch
    {
        ChangePlanArtifactKind.File => "file",
        ChangePlanArtifactKind.ProjectFile => "projectFile",
        ChangePlanArtifactKind.RoslynDocument => "roslynDocument",
        ChangePlanArtifactKind.AdditionalDocument => "additionalDocument",
        ChangePlanArtifactKind.AnalyzerConfigDocument => "analyzerConfigDocument",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string WireName(ChangePlanPathKind value) => value switch
    {
        ChangePlanPathKind.Missing => "missing",
        ChangePlanPathKind.RegularFile => "regularFile",
        ChangePlanPathKind.Directory => "directory",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string WireName(ChangePlanGuardKind value) => value switch
    {
        ChangePlanGuardKind.ExternalWriteGuard => "externalWriteGuard",
        ChangePlanGuardKind.ConditionalEffect => "conditionalEffect",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string WireName(ChangePlanDependencyKind value) => value switch
    {
        ChangePlanDependencyKind.SemanticInput => "semanticInput",
        ChangePlanDependencyKind.ProjectInput => "projectInput",
        ChangePlanDependencyKind.DirectoryEntry => "directoryEntry",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string WireName(MutationDiagnosticSeverity value) => value switch
    {
        MutationDiagnosticSeverity.Information => "information",
        MutationDiagnosticSeverity.Warning => "warning",
        MutationDiagnosticSeverity.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string WireName(WorkspaceCompletenessState value) => value switch
    {
        WorkspaceCompletenessState.Complete => "complete",
        WorkspaceCompletenessState.Partial => "partial",
        WorkspaceCompletenessState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string WireName(ChangePlanCoordinationDomain value) => value switch
    {
        ChangePlanCoordinationDomain.ExclusivePrincipal => "exclusivePrincipal",
        ChangePlanCoordinationDomain.SharedAuthority => "sharedAuthority",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string WireName(ChangePlanCoordinationMode value) => value switch
    {
        ChangePlanCoordinationMode.ExternalWriteGuards => "externalWriteGuards",
        ChangePlanCoordinationMode.ConditionalEffects => "conditionalEffects",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string WireName(ChangePlanCaseSensitivity value) => value switch
    {
        ChangePlanCaseSensitivity.Sensitive => "sensitive",
        ChangePlanCaseSensitivity.Insensitive => "insensitive",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

}

internal sealed class ChangePlanEnvelope
{
    private readonly byte[] _canonicalPayloadBytes;

    private ChangePlanEnvelope(
        ChangePlanPayload payload,
        string planHash,
        byte[] canonicalPayloadBytes)
    {
        Payload = payload;
        PlanHash = planHash;
        _canonicalPayloadBytes = [.. canonicalPayloadBytes];
    }

    internal ChangePlanPayload Payload { get; }
    internal string PlanHash { get; }
    internal byte[] CanonicalPayloadBytes => [.. _canonicalPayloadBytes];

    internal byte[] SerializeEnvelope()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("planHash", PlanHash);
            writer.WritePropertyName("payload");
            writer.WriteRawValue(_canonicalPayloadBytes);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    internal static ChangePlanEnvelope Create(ChangePlanPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var bytes = CanonicalChangePlan.Serialize(payload);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ChangePlanEnvelope(payload, hash, bytes);
    }

    internal static ChangePlanEnvelope FromStored(
        ChangePlanPayload payload,
        string claimedHash,
        byte[] canonicalPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(claimedHash);
        ArgumentNullException.ThrowIfNull(canonicalPayloadBytes);
        return new ChangePlanEnvelope(payload, claimedHash, canonicalPayloadBytes);
    }

    internal bool Verify()
    {
        var computedBytes = CanonicalChangePlan.Serialize(Payload);
        return CryptographicOperations.FixedTimeEquals(computedBytes, _canonicalPayloadBytes)
               && CanonicalChangePlan.VerifyHash(Payload, PlanHash);
    }
}
