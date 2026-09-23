using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RoslynTools.Core;

internal enum ChangePlanEnvelopeReadErrorKind
{
    Malformed,
    SchemaIncompatible,
    IntegrityFailure
}

internal sealed record ChangePlanEnvelopeReadError(
    ChangePlanEnvelopeReadErrorKind Kind,
    string Message);

/// <summary>
///     Rehydrates the immutable plan model from persisted canonical bytes. Successful
///     reads require byte-for-byte canonical payload and envelope projections, so a
///     permissive JSON representation can never acquire plan identity.
/// </summary>
internal static class CanonicalChangePlanReader
{
    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    internal static Result<ChangePlanEnvelope, ChangePlanEnvelopeReadError> ReadEnvelope(
        byte[] envelopeBytes)
    {
        ArgumentNullException.ThrowIfNull(envelopeBytes);
        try
        {
            using var document = JsonDocument.Parse(envelopeBytes);
            // Stryker disable once String : The label changes diagnostic prose only; malformed-root typing is asserted separately.
            var root = RequireObject(document.RootElement, "envelope");
            var claimedHash = RequiredString(root, "planHash");
            var payloadElement = RequiredProperty(root, "payload");
            var schemaVersion = RequiredString(payloadElement, "schemaVersion");
            if (!string.Equals(schemaVersion, ChangePlanSchema.Version, StringComparison.Ordinal))
            {
                return Result<ChangePlanEnvelope, ChangePlanEnvelopeReadError>.Err(new(
                    ChangePlanEnvelopeReadErrorKind.SchemaIncompatible,
                    $"Unsupported change-plan schema '{schemaVersion}'."));
            }

            var payloadBytes = Encoding.UTF8.GetBytes(payloadElement.GetRawText());
            var payload = ReadPayload(payloadElement);
            var envelope = ChangePlanEnvelope.FromStored(payload, claimedHash, payloadBytes);
            if (!envelope.Verify())
            {
                return Result<ChangePlanEnvelope, ChangePlanEnvelopeReadError>.Err(new(
                    ChangePlanEnvelopeReadErrorKind.IntegrityFailure,
                    "The persisted change-plan payload or claimed hash failed canonical verification."));
            }

            if (!envelope.SerializeEnvelope().AsSpan().SequenceEqual(envelopeBytes))
            {
                return Result<ChangePlanEnvelope, ChangePlanEnvelopeReadError>.Err(new(
                    ChangePlanEnvelopeReadErrorKind.IntegrityFailure,
                    "The persisted change-plan envelope is not the canonical byte projection."));
            }

            return Result<ChangePlanEnvelope, ChangePlanEnvelopeReadError>.Ok(envelope);
        }
        catch (Exception exception) when (exception is JsonException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or FormatException
                                           or OverflowException)
        {
            return Result<ChangePlanEnvelope, ChangePlanEnvelopeReadError>.Err(new(
                ChangePlanEnvelopeReadErrorKind.Malformed,
                $"The persisted change-plan envelope is malformed: {exception.Message}"));
        }
    }

    private static ChangePlanPayload ReadPayload(JsonElement root)
    {
        var repository = RequireObject(RequiredProperty(root, "repository"), "repository");
        var coordination = RequireObject(RequiredProperty(root, "coordination"), "coordination");
        var pathPolicy = RequireObject(RequiredProperty(root, "pathPolicy"), "pathPolicy");

        return new ChangePlanPayload(
            Guid.Parse(RequiredString(root, "planId")),
            new ChangePlanRepository(
                RequiredString(repository, "id"),
                RequiredString(repository, "root"),
                RequiredString(repository, "resolvedRoot"),
                RequiredString(repository, "rootPlatformIdentity"),
                RequiredString(repository, "baseRevision"),
                RequiredString(repository, "sourceFingerprint")),
            ParseTimestamp(RequiredString(root, "createdAtUtc")),
            ParseTimestamp(RequiredString(root, "expiresAtUtc")),
            ReadArray(root, "operations", ReadOperation),
            ReadArray(root, "dependencies", ReadDependency),
            ReadArray(root, "effects", ReadEffect),
            ReadArray(root, "diagnostics", ReadDiagnostic),
            ReadStringArray(root, "assumptions"),
            ReadConfidence(RequiredProperty(root, "confidence")),
            ReadStringArray(root, "limitations"),
            ReadWorkspace(RequiredProperty(root, "workspace")),
            ReadArray(root, "verificationActions", ReadVerification),
            new ChangePlanCoordination(
                ParseCoordinationDomain(RequiredString(coordination, "domain")),
                ParseCoordinationMode(RequiredString(coordination, "mode")),
                RequiredString(coordination, "writerSetProof"),
                RequiredString(coordination, "controlRoot"),
                RequiredString(coordination, "controlRootResolvedPath"),
                RequiredString(coordination, "controlRootPlatformIdentity"),
                RequiredString(coordination, "filesystemVolumeIdentity"),
                RequiredString(coordination, "stagingRoot"),
                RequiredString(coordination, "stagingRootResolvedPath"),
                RequiredString(coordination, "stagingRootPlatformIdentity"),
                RequiredString(coordination, "backupRoot"),
                RequiredString(coordination, "backupRootResolvedPath"),
                RequiredString(coordination, "backupRootPlatformIdentity")),
            new ChangePlanPathPolicy(
                ParseCaseSensitivity(RequiredString(pathPolicy, "caseSensitivity")),
                ParseFixed(
                    RequiredString(pathPolicy, "longPath"),
                    "reject",
                    ChangePlanLongPathPolicy.Reject),
                ParseFixed(
                    RequiredString(pathPolicy, "readOnly"),
                    "reject",
                    ChangePlanReadOnlyPolicy.Reject),
                ParseFixed(
                    RequiredString(pathPolicy, "symlink"),
                    "reject",
                    ChangePlanSymlinkPolicy.Reject),
                ParseFixed(
                    RequiredString(pathPolicy, "hardLink"),
                    "reject",
                    ChangePlanHardLinkPolicy.Reject)),
            ParseFixed(
                RequiredString(root, "cleanupPolicy"),
                "retainUntilVerifiedTerminal",
                ChangePlanCleanupPolicy.RetainUntilVerifiedTerminal),
            ParseFixed(
                RequiredString(root, "recoveryPolicy"),
                "requireDurableJournal",
                ChangePlanRecoveryPolicy.RequireDurableJournal));
    }

    private static ChangePlanOperation ReadOperation(JsonElement element)
    {
        var value = RequireObject(element, "operation");
        return new ChangePlanOperation(
            RequiredInt32(value, "sequence"),
            RequiredString(value, "name"),
            RequiredString(value, "stableInput"),
            ReadArray(value, "arguments", ReadValue));
    }

    private static ChangePlanDependency ReadDependency(JsonElement element)
    {
        var value = RequireObject(element, "dependency");
        return new ChangePlanDependency(
            RequiredString(value, "path"),
            RequiredString(value, "resolvedPath"),
            ParseDependencyKind(RequiredString(value, "kind")),
            ReadPathState(RequiredProperty(value, "expectedState")),
            ReadGuard(RequiredProperty(value, "guard")));
    }

    private static ChangePlanEffect ReadEffect(JsonElement element)
    {
        var value = RequireObject(element, "effect");
        return new ChangePlanEffect(
            RequiredInt32(value, "sequence"),
            ParseEffectKind(RequiredString(value, "kind")),
            ParseArtifactKind(RequiredString(value, "artifactKind")),
            RequiredString(value, "sourcePath"),
            RequiredString(value, "sourceResolvedPath"),
            NullableString(value, "destinationPath"),
            NullableString(value, "destinationResolvedPath"),
            ReadPathState(RequiredProperty(value, "sourceBefore")),
            ReadPathState(RequiredProperty(value, "sourceAfter")),
            ReadNullablePathState(value, "destinationBefore"),
            ReadNullablePathState(value, "destinationAfter"),
            NullableBase64(value, "originalContentBase64"),
            NullableBase64(value, "replacementContentBase64"),
            ReadArray(value, "guards", ReadGuard));
    }

    private static ChangePlanPathState? ReadNullablePathState(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : ReadPathState(value);
    }

    private static ChangePlanPathState ReadPathState(JsonElement element)
    {
        var value = RequireObject(element, "path state");
        return new ChangePlanPathState(
            RequiredBoolean(value, "exists"),
            ParsePathKind(RequiredString(value, "kind")),
            NullableString(value, "contentHash"),
            NullableString(value, "platformFileIdentity"),
            NullableInt32(value, "linkCount"),
            ReadStringArray(value, "knownHardLinkAliases"),
            NullableString(value, "encodingName"),
            NullableBoolean(value, "hasByteOrderMark"),
            NullableString(value, "newline"),
            NullableString(value, "fileMode"),
            NullableBoolean(value, "executable"),
            NullableBoolean(value, "readOnly"),
            NullableString(value, "symlinkTarget"));
    }

    private static ChangePlanGuardRequirement ReadGuard(JsonElement element)
    {
        var value = RequireObject(element, "guard");
        if (!RequiredBoolean(value, "required"))
            throw new FormatException("A persisted change-plan guard must be required.");
        return new ChangePlanGuardRequirement(
            RequiredString(value, "path"),
            ParseGuardKind(RequiredString(value, "kind")),
            RequiredString(value, "expectedToken"));
    }

    private static ChangePlanDiagnostic ReadDiagnostic(JsonElement element)
    {
        var value = RequireObject(element, "diagnostic");
        return new ChangePlanDiagnostic(
            RequiredString(value, "code"),
            ParseDiagnosticSeverity(RequiredString(value, "severity")),
            RequiredString(value, "message"),
            ReadArray(value, "context", ReadValue));
    }

    private static ChangePlanValue ReadValue(JsonElement element)
    {
        var value = RequireObject(element, "name/value pair");
        return new ChangePlanValue(
            RequiredString(value, "name"),
            NullableString(value, "value"));
    }

    private static ChangePlanConfidence ReadConfidence(JsonElement element)
    {
        var value = RequireObject(element, "confidence");
        return new ChangePlanConfidence(
            RequiredString(value, "level"),
            RequiredString(value, "basis"),
            ReadStringArray(value, "evidence"));
    }

    private static ChangePlanWorkspace ReadWorkspace(JsonElement element)
    {
        var value = RequireObject(element, "workspace");
        return new ChangePlanWorkspace(
            ParseWorkspaceState(RequiredString(value, "state")),
            RequiredString(value, "solutionPath"),
            RequiredString(value, "configuration"),
            ReadStringArray(value, "targetFrameworks"),
            RequiredString(value, "sdkIdentity"),
            RequiredString(value, "compilerIdentity"),
            RequiredString(value, "evaluatorMode"),
            RequiredString(value, "trustState"));
    }

    private static ChangePlanVerificationAction ReadVerification(JsonElement element)
    {
        var value = RequireObject(element, "verification action");
        return new ChangePlanVerificationAction(
            RequiredInt32(value, "sequence"),
            RequiredString(value, "name"),
            RequiredString(value, "capability"),
            RequiredString(value, "expectedOutcome"),
            RequiredBoolean(value, "required"));
    }

    private static T[] ReadArray<T>(
        JsonElement parent,
        string propertyName,
        Func<JsonElement, T> read)
    {
        var array = RequiredProperty(parent, propertyName);
        if (array.ValueKind != JsonValueKind.Array)
            throw new FormatException($"'{propertyName}' must be an array.");
        return array.EnumerateArray().Select(read).ToArray();
    }

    private static string[] ReadStringArray(JsonElement parent, string propertyName) =>
        ReadArray(parent, propertyName, element =>
        {
            // Stryker disable once Conditional : GetString throws the same caught malformed-envelope failure for every non-string JSON kind.
            if (element.ValueKind != JsonValueKind.String)
                throw new FormatException($"'{propertyName}' must contain only strings.");
            return element.GetString()!;
        });

    private static JsonElement RequiredProperty(JsonElement parent, string propertyName)
    {
        // Stryker disable once Statement : TryGetProperty itself throws the same caught malformed-envelope failure when parent is not an object.
        RequireObject(parent, propertyName);
        if (!parent.TryGetProperty(propertyName, out var value))
            // Stryker disable once Statement : Removing this throw reaches the scalar validator and returns the same typed malformed result for the same property.
            throw new FormatException($"The required property '{propertyName}' is missing.");
        return value;
    }

    private static JsonElement RequireObject(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new FormatException($"The {description} must be a JSON object.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        if (value.ValueKind != JsonValueKind.String)
            throw new FormatException($"'{propertyName}' must be a string.");
        return value.GetString()!;
    }

    private static string? NullableString(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new FormatException($"'{propertyName}' must be a string or null.")
        };
    }

    private static byte[]? NullableBase64(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetBytesFromBase64(),
            _ => throw new FormatException($"'{propertyName}' must be base64 or null.")
        };
    }

    private static int RequiredInt32(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : throw new FormatException($"'{propertyName}' must be a 32-bit integer.");
    }

    private static int? NullableInt32(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : throw new FormatException($"'{propertyName}' must be a 32-bit integer or null.");
    }

    private static bool RequiredBoolean(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException($"'{propertyName}' must be a boolean.")
        };
    }

    private static bool? NullableBoolean(JsonElement parent, string propertyName)
    {
        var value = RequiredProperty(parent, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException($"'{propertyName}' must be a boolean or null.")
        };
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(
            value,
            UtcTimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static T ParseFixed<T>(string value, string expected, T result) =>
        string.Equals(value, expected, StringComparison.Ordinal)
            ? result
            : throw new FormatException($"Unsupported value '{value}'; expected '{expected}'.");

    private static ChangePlanEffectKind ParseEffectKind(string value) => value switch
    {
        "create" => ChangePlanEffectKind.Create,
        "replace" => ChangePlanEffectKind.Replace,
        "delete" => ChangePlanEffectKind.Delete,
        "move" => ChangePlanEffectKind.Move,
        _ => throw new FormatException($"Unsupported effect kind '{value}'.")
    };

    private static ChangePlanArtifactKind ParseArtifactKind(string value) => value switch
    {
        "file" => ChangePlanArtifactKind.File,
        "projectFile" => ChangePlanArtifactKind.ProjectFile,
        "roslynDocument" => ChangePlanArtifactKind.RoslynDocument,
        "additionalDocument" => ChangePlanArtifactKind.AdditionalDocument,
        "analyzerConfigDocument" => ChangePlanArtifactKind.AnalyzerConfigDocument,
        _ => throw new FormatException($"Unsupported artifact kind '{value}'.")
    };

    private static ChangePlanPathKind ParsePathKind(string value) => value switch
    {
        "missing" => ChangePlanPathKind.Missing,
        "regularFile" => ChangePlanPathKind.RegularFile,
        "directory" => ChangePlanPathKind.Directory,
        _ => throw new FormatException($"Unsupported path kind '{value}'.")
    };

    private static ChangePlanGuardKind ParseGuardKind(string value) => value switch
    {
        "externalWriteGuard" => ChangePlanGuardKind.ExternalWriteGuard,
        "conditionalEffect" => ChangePlanGuardKind.ConditionalEffect,
        _ => throw new FormatException($"Unsupported guard kind '{value}'.")
    };

    private static ChangePlanDependencyKind ParseDependencyKind(string value) => value switch
    {
        "semanticInput" => ChangePlanDependencyKind.SemanticInput,
        "projectInput" => ChangePlanDependencyKind.ProjectInput,
        "directoryEntry" => ChangePlanDependencyKind.DirectoryEntry,
        _ => throw new FormatException($"Unsupported dependency kind '{value}'.")
    };

    private static MutationDiagnosticSeverity ParseDiagnosticSeverity(string value) => value switch
    {
        "information" => MutationDiagnosticSeverity.Information,
        "warning" => MutationDiagnosticSeverity.Warning,
        "error" => MutationDiagnosticSeverity.Error,
        _ => throw new FormatException($"Unsupported diagnostic severity '{value}'.")
    };

    private static WorkspaceCompletenessState ParseWorkspaceState(string value) => value switch
    {
        "complete" => WorkspaceCompletenessState.Complete,
        "partial" => WorkspaceCompletenessState.Partial,
        "failed" => WorkspaceCompletenessState.Failed,
        _ => throw new FormatException($"Unsupported workspace state '{value}'.")
    };

    private static ChangePlanCoordinationDomain ParseCoordinationDomain(string value) => value switch
    {
        "exclusivePrincipal" => ChangePlanCoordinationDomain.ExclusivePrincipal,
        "sharedAuthority" => ChangePlanCoordinationDomain.SharedAuthority,
        _ => throw new FormatException($"Unsupported coordination domain '{value}'.")
    };

    private static ChangePlanCoordinationMode ParseCoordinationMode(string value) => value switch
    {
        "externalWriteGuards" => ChangePlanCoordinationMode.ExternalWriteGuards,
        "conditionalEffects" => ChangePlanCoordinationMode.ConditionalEffects,
        _ => throw new FormatException($"Unsupported coordination mode '{value}'.")
    };

    private static ChangePlanCaseSensitivity ParseCaseSensitivity(string value) => value switch
    {
        "sensitive" => ChangePlanCaseSensitivity.Sensitive,
        "insensitive" => ChangePlanCaseSensitivity.Insensitive,
        _ => throw new FormatException($"Unsupported case-sensitivity policy '{value}'.")
    };
}
