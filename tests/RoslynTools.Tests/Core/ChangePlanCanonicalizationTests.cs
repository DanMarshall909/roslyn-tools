using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class ChangePlanCanonicalizationTests
{
    [Fact]
    public void canonical_payload_has_a_stable_conformance_hash_and_no_hash_field()
    {
        var payload = CreatePayload();

        var canonicalBytes = CanonicalChangePlan.Serialize(payload);
        var canonicalJson = Encoding.UTF8.GetString(canonicalBytes);
        var envelope = ChangePlanEnvelope.Create(payload);

        canonicalJson.ShouldNotContain("planHash", Case.Insensitive);
        envelope.PlanHash.ShouldBe("5061e76ee8e4b5e5fc17dbce4815aa22f2b3b7e55485efc3c2ad3a066aff5826");
        envelope.CanonicalPayloadBytes.ShouldBe(canonicalBytes);
        Encoding.UTF8.GetString(envelope.SerializeEnvelope()).ShouldStartWith(
            $"{{\"planHash\":\"{envelope.PlanHash}\",\"payload\":{{\"schemaVersion\":\"rt.change-plan.v1\"");
        envelope.Verify().ShouldBeTrue();
        CanonicalChangePlan.VerifyHash(payload, envelope.PlanHash).ShouldBeTrue();
    }

    [Fact]
    public void changing_only_the_claimed_hash_cannot_change_the_hashed_payload()
    {
        var payload = CreatePayload();
        var canonicalBytes = CanonicalChangePlan.Serialize(payload);
        var invalid = ChangePlanEnvelope.FromStored(
            payload,
            new string('0', 64),
            canonicalBytes);

        invalid.CanonicalPayloadBytes.ShouldBe(canonicalBytes);
        invalid.Verify().ShouldBeFalse();
        CanonicalChangePlan.VerifyHash(payload, "not-a-sha256-hash").ShouldBeFalse();
        ChangePlanEnvelope.FromStored(
            payload,
            claimedHash: ChangePlanEnvelope.Create(payload).PlanHash.ToUpperInvariant(),
            canonicalPayloadBytes: canonicalBytes).Verify().ShouldBeFalse();
    }

    [Fact]
    public void persisted_payload_bytes_must_match_the_reserialized_payload_exactly()
    {
        var payload = CreatePayload();
        var reviewed = ChangePlanEnvelope.Create(payload);
        var corruptedBytes = reviewed.CanonicalPayloadBytes;
        corruptedBytes[^1] ^= 1;
        var corrupted = ChangePlanEnvelope.FromStored(
            payload,
            reviewed.PlanHash,
            corruptedBytes);

        corrupted.Verify().ShouldBeFalse();
        reviewed.Verify().ShouldBeTrue();
    }

    [Fact]
    public void canonicalization_normalizes_paths_and_orders_set_like_values()
    {
        var first = CreatePayload(
            sourcePath: "src\\Feature.cs",
            operationArguments:
            [
                new ChangePlanValue("newName", "After"),
                new ChangePlanValue("kind", null),
                new ChangePlanValue("symbol", "Example.Before")
            ],
            assumptions: ["The repository remains exclusively owned.", "The selected symbol is stable."],
            limitations: ["Generated callers are unavailable.", "External consumers are unknown."]);
        var second = CreatePayload(
            sourcePath: "src/Feature.cs",
            operationArguments:
            [
                new ChangePlanValue("symbol", "Example.Before"),
                new ChangePlanValue("newName", "After"),
                new ChangePlanValue("kind", null)
            ],
            assumptions: ["The selected symbol is stable.", "The repository remains exclusively owned."],
            limitations: ["External consumers are unknown.", "Generated callers are unavailable."]);

        CanonicalChangePlan.Serialize(first).ShouldBe(CanonicalChangePlan.Serialize(second));
        ChangePlanEnvelope.Create(first).PlanHash.ShouldBe(ChangePlanEnvelope.Create(second).PlanHash);
        first.Effects.ShouldHaveSingleItem().SourcePath.ShouldBe("src/Feature.cs");
    }

    [Fact]
    public void payload_and_nested_collections_are_immutable_snapshots()
    {
        var content = Encoding.UTF8.GetBytes("class After;\n");
        var assumptions = new List<string> { "The selected symbol is stable." };
        var effect = CreateReplacementEffect("src/Feature.cs", content);
        var payload = CreatePayload(
            replacementContent: content,
            effects: [effect],
            assumptions: assumptions);
        var before = ChangePlanEnvelope.Create(payload).PlanHash;

        content[0] = (byte)'X';
        assumptions[0] = "changed";

        ChangePlanEnvelope.Create(payload).PlanHash.ShouldBe(before);
        payload.Assumptions.ShouldBe(["The selected symbol is stable."]);
        payload.Effects[0].ReplacementContent.ToArray()
            .ShouldBe(Encoding.UTF8.GetBytes("class After;\n"));
    }

    [Fact]
    public void changing_effect_bytes_invalidates_the_reviewed_hash_and_changes_the_derived_diff()
    {
        var original = CreatePayload();
        var changed = CreatePayload(replacementContent: Encoding.UTF8.GetBytes("class ChangedAgain;\n"));
        var reviewed = ChangePlanEnvelope.Create(original);

        CanonicalChangePlan.VerifyHash(changed, reviewed.PlanHash).ShouldBeFalse();
        changed.Diff.ShouldNotBe(original.Diff);
        changed.Diff.ShouldContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("class ChangedAgain;\n")));
    }

    [Fact]
    public void payload_rejects_duplicate_or_conflicting_paths_and_arguments()
    {
        var first = CreateReplacementEffect("src/Feature.cs", Encoding.UTF8.GetBytes("class After;\n"));
        var second = CreateReplacementEffect("src/./Feature.cs", Encoding.UTF8.GetBytes("class Newer;\n"), 2);

        Should.Throw<ArgumentException>(() => CreatePayload(effects: [first, second]))
            .Message.ShouldContain("more than one effect");
        Should.Throw<ArgumentException>(() => new ChangePlanOperation(
            1,
            "rename",
            "Example.Before",
            [new ChangePlanValue("symbol", "one"), new ChangePlanValue("symbol", "two")]))
            .Message.ShouldContain("unique");
        Should.Throw<ArgumentException>(() => CreateReplacementEffect(
            "../outside.cs",
            Encoding.UTF8.GetBytes("class After;\n")))
            .Message.ShouldContain("repository-relative");
    }

    [Fact]
    public void replacement_content_must_match_the_declared_postcondition()
    {
        var content = Encoding.UTF8.GetBytes("class After;\n");
        var before = ExistingFile("class Before;\n");
        var wrongAfter = ExistingFile("different");

        Should.Throw<ArgumentException>(() => new ChangePlanEffect(
            1,
            ChangePlanEffectKind.Replace,
            ChangePlanArtifactKind.RoslynDocument,
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            destinationPath: null,
            destinationResolvedPath: null,
            sourceBefore: before,
            sourceAfter: wrongAfter,
            destinationBefore: null,
            destinationAfter: null,
            originalContent: Encoding.UTF8.GetBytes("class Before;\n"),
            replacementContent: content,
            guards: [new ChangePlanGuardRequirement(
                "src/Feature.cs",
                ChangePlanGuardKind.ExternalWriteGuard,
                expectedToken: "guard-v1")]))
            .Message.ShouldContain("postcondition hash");
    }

    [Fact]
    public void every_affected_path_requires_one_guard_requirement()
    {
        var content = Encoding.UTF8.GetBytes("class After;\n");

        Should.Throw<ArgumentException>(() => new ChangePlanEffect(
            1,
            ChangePlanEffectKind.Replace,
            ChangePlanArtifactKind.RoslynDocument,
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            destinationPath: null,
            destinationResolvedPath: null,
            sourceBefore: ExistingFile("class Before;\n"),
            sourceAfter: ExistingFile(content),
            destinationBefore: null,
            destinationAfter: null,
            originalContent: Encoding.UTF8.GetBytes("class Before;\n"),
            replacementContent: content,
            guards: []))
            .Message.ShouldContain("each affected path");
    }

    [Fact]
    public void payload_requires_unique_guarded_read_dependencies()
    {
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: []))
            .Message.ShouldContain("read dependency");

        var dependency = CreateDirectoryDependency("src");
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [dependency, dependency]))
            .Message.ShouldContain("declared more than once");
    }

    [Fact]
    public void every_guard_must_match_the_coordination_mode()
    {
        var conditionalDependency = CreateDirectoryDependency(
            "src",
            guardKind: ChangePlanGuardKind.ConditionalEffect);

        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [conditionalDependency]))
            .Message.ShouldContain("coordination mode");

        var conditionalEffect = CreateReplacementEffect(
            "src/Feature.cs",
            Encoding.UTF8.GetBytes("class After;\n"),
            guards:
            [
                new ChangePlanGuardRequirement(
                    "src/Feature.cs",
                    ChangePlanGuardKind.ConditionalEffect,
                    "conditional-v1")
            ]);
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [conditionalEffect]))
            .Message.ShouldContain("coordination mode");
    }

    [Theory]
    [InlineData("src/Feature.cs:stream")]
    [InlineData("src/CON.cs")]
    [InlineData("src/trailing. ")]
    public void special_or_nonportable_path_segments_are_rejected(string path)
    {
        Should.Throw<ArgumentException>(() => CreateReplacementEffect(
            path,
            Encoding.UTF8.GetBytes("class After;\n")))
            .Message.ShouldContain("non-portable");
    }

    [Fact]
    public void effects_are_restricted_to_regular_files()
    {
        var directory = DirectoryState("device:42;inode:84");

        Should.Throw<ArgumentException>(() => new ChangePlanEffect(
            1,
            ChangePlanEffectKind.Delete,
            ChangePlanArtifactKind.File,
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            destinationPath: null,
            destinationResolvedPath: null,
            sourceBefore: directory,
            sourceAfter: ChangePlanPathState.Missing,
            destinationBefore: null,
            destinationAfter: null,
            originalContent: [],
            replacementContent: null,
            guards: [ExternalGuard("src/Feature.cs")]))
            .Message.ShouldContain("regular files");
    }

    [Fact]
    public void affected_paths_cannot_overlap_or_share_a_platform_identity()
    {
        var nested = new[]
        {
            CreateReplacementEffect("src/Feature.cs", Encoding.UTF8.GetBytes("class After;\n")),
            CreateReplacementEffect("src/Feature.cs/Generated.cs", Encoding.UTF8.GetBytes("class Generated;\n"), 2)
        };
        Should.Throw<ArgumentException>(() => CreatePayload(effects: nested))
            .Message.ShouldContain("ancestor/descendant");

        var aliased = new[]
        {
            CreateReplacementEffect("src/Feature.cs", Encoding.UTF8.GetBytes("class After;\n")),
            CreateReplacementEffect("src/Other.cs", Encoding.UTF8.GetBytes("class Other;\n"), 2)
        };
        Should.Throw<ArgumentException>(() => CreatePayload(effects: aliased))
            .Message.ShouldContain("platform file identity");

        var caseVariantAncestor = new[]
        {
            CreateReplacementEffect(
                "SRC",
                Encoding.UTF8.GetBytes("class Parent;\n"),
                sourceBefore: ExistingFile("class Before;\n", "device:42;inode:500")),
            CreateReplacementEffect(
                "src/Child.cs",
                Encoding.UTF8.GetBytes("class Child;\n"),
                2,
                sourceBefore: ExistingFile("class Before;\n", "device:42;inode:501"))
        };
        Should.Throw<ArgumentException>(() => CreatePayload(
            effects: caseVariantAncestor,
            dependencies: [CreateDirectoryDependency("inputs")],
            pathPolicy: RejectPathPolicy(ChangePlanCaseSensitivity.Insensitive)))
            .Message.ShouldContain("ancestor/descendant");
    }

    [Fact]
    public void exact_metadata_and_single_link_preconditions_are_required()
    {
        var missingMetadata = ExistingFile("class Before;\n", encodingName: null);
        Should.Throw<ArgumentException>(() => CreatePayload(effects:
            [CreateReplacementEffect(
                "src/Feature.cs",
                Encoding.UTF8.GetBytes("class After;\n"),
                sourceBefore: missingMetadata)]))
            .Message.ShouldContain("UTF-8");

        var hardLinked = ExistingFile("class Before;\n", linkCount: 2);
        Should.Throw<ArgumentException>(() => CreatePayload(effects:
            [CreateReplacementEffect(
                "src/Feature.cs",
                Encoding.UTF8.GetBytes("class After;\n"),
                sourceBefore: hardLinked)]))
            .Message.ShouldContain("one proven link");
    }

    [Fact]
    public void resolved_paths_must_prove_the_lexical_repository_location()
    {
        var effect = CreateReplacementEffect(
            "src/Feature.cs",
            Encoding.UTF8.GetBytes("class After;\n"),
            sourceResolvedPath: "/workspace/elsewhere/Feature.cs");

        Should.Throw<ArgumentException>(() => CreatePayload(effects: [effect]))
            .Message.ShouldContain("unsymlinked repository path");
    }

    [Fact]
    public void create_delete_and_move_effects_have_complete_canonical_payloads()
    {
        var createdBytes = Encoding.UTF8.GetBytes("created\n");
        var create = new ChangePlanEffect(
            1,
            ChangePlanEffectKind.Create,
            ChangePlanArtifactKind.File,
            "src/Created.cs",
            "/workspace/repository/src/Created.cs",
            destinationPath: null,
            destinationResolvedPath: null,
            sourceBefore: ChangePlanPathState.Missing,
            sourceAfter: ExistingFile(createdBytes, platformFileIdentity: null),
            destinationBefore: null,
            destinationAfter: null,
            originalContent: null,
            replacementContent: createdBytes,
            guards: [ExternalGuard("src/Created.cs")]);
        var delete = new ChangePlanEffect(
            1,
            ChangePlanEffectKind.Delete,
            ChangePlanArtifactKind.File,
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            destinationPath: null,
            destinationResolvedPath: null,
            sourceBefore: ExistingFile("class Before;\n"),
            sourceAfter: ChangePlanPathState.Missing,
            destinationBefore: null,
            destinationAfter: null,
            originalContent: Encoding.UTF8.GetBytes("class Before;\n"),
            replacementContent: null,
            guards: [ExternalGuard("src/Feature.cs")]);
        var move = new ChangePlanEffect(
            1,
            ChangePlanEffectKind.Move,
            ChangePlanArtifactKind.File,
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            destinationPath: "src/Moved.cs",
            destinationResolvedPath: "/workspace/repository/src/Moved.cs",
            sourceBefore: ExistingFile("class Before;\n"),
            sourceAfter: ChangePlanPathState.Missing,
            destinationBefore: ChangePlanPathState.Missing,
            destinationAfter: ExistingFile("class Before;\n", platformFileIdentity: null),
            originalContent: Encoding.UTF8.GetBytes("class Before;\n"),
            replacementContent: null,
            guards: [ExternalGuard("src/Feature.cs"), ExternalGuard("src/Moved.cs")]);

        foreach (var effect in new[] { create, delete, move })
        {
            var payload = CreatePayload(effects: [effect]);
            ChangePlanEnvelope.Create(payload).Verify().ShouldBeTrue();
            payload.Diff.ShouldContain($"effect 1 {effect.Kind.ToString().ToLowerInvariant()}");
        }
    }

    [Fact]
    public void protected_roots_use_the_plan_case_policy_for_distinctness()
    {
        var coordination = new ChangePlanCoordination(
            ChangePlanCoordinationDomain.ExclusivePrincipal,
            ChangePlanCoordinationMode.ExternalWriteGuards,
            "single-principal plus complete affected-path guard set",
            "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555",
            "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555",
            "device:99;inode:1",
            "device:99",
            "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555/Stage",
            "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555/Stage",
            "device:99;inode:2",
            "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555/stage",
            "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555/stage",
            "device:99;inode:3");

        Should.Throw<ArgumentException>(() => CreatePayload(
            coordination: coordination,
            pathPolicy: new ChangePlanPathPolicy(
                ChangePlanCaseSensitivity.Insensitive,
                ChangePlanLongPathPolicy.Reject,
                ChangePlanReadOnlyPolicy.Reject,
                ChangePlanSymlinkPolicy.Reject,
                ChangePlanHardLinkPolicy.Reject)))
            .Message.ShouldContain("distinct");
    }

    [Fact]
    public void protected_roots_must_record_their_unsymlinked_physical_locations()
    {
        var coordination = CreateCoordination(
            controlRootResolvedPath: "/workspace/repository/.rt-control");

        Should.Throw<ArgumentException>(() => CreatePayload(coordination: coordination))
            .Message.ShouldContain("resolved path");
    }

    [Fact]
    public void directory_dependencies_accept_their_observed_posix_link_count()
    {
        var dependency = CreateDirectoryDependency("src", linkCount: 5);

        Should.NotThrow(() => CreatePayload(dependencies: [dependency]));
    }

    [Fact]
    public void diagnostics_are_a_canonical_duplicate_free_set()
    {
        var first = new ChangePlanDiagnostic(
            "rename.preview",
            MutationDiagnosticSeverity.Information,
            "Reviewed diagnostic.",
            [new ChangePlanValue("symbol", "A")]);
        var second = new ChangePlanDiagnostic(
            "rename.preview",
            MutationDiagnosticSeverity.Information,
            "Reviewed diagnostic.",
            [new ChangePlanValue("symbol", "B")]);

        var canonical = CreatePayload(diagnostics: [first, second]);
        var reversed = CreatePayload(diagnostics: [second, first]);

        CanonicalChangePlan.Serialize(canonical).ShouldBe(CanonicalChangePlan.Serialize(reversed));
        Should.Throw<ArgumentException>(() => CreatePayload(diagnostics: [first, first]))
            .Message.ShouldContain("unique");
    }

    [Fact]
    public void expiry_is_evaluated_at_an_explicit_utc_instant()
    {
        var payload = CreatePayload();

        payload.IsExpired(new DateTimeOffset(2030, 1, 2, 3, 9, 4, TimeSpan.Zero)).ShouldBeFalse();
        payload.IsExpired(new DateTimeOffset(2030, 1, 2, 3, 9, 5, TimeSpan.Zero)).ShouldBeTrue();
    }

    [Fact]
    public void canonical_json_records_every_reviewed_field_with_exact_wire_tokens()
    {
        using var document = JsonDocument.Parse(CanonicalChangePlan.Serialize(CreatePayload()));
        var root = document.RootElement;

        root.GetProperty("schemaVersion").GetString().ShouldBe("rt.change-plan.v1");
        root.GetProperty("planId").GetString().ShouldBe("11111111-2222-3333-4444-555555555555");
        root.GetProperty("createdAtUtc").GetString().ShouldBe("2030-01-02T03:04:05.0000000Z");
        root.GetProperty("expiresAtUtc").GetString().ShouldBe("2030-01-02T03:09:05.0000000Z");
        root.GetProperty("cleanupPolicy").GetString().ShouldBe("retainUntilVerifiedTerminal");
        root.GetProperty("recoveryPolicy").GetString().ShouldBe("requireDurableJournal");

        var repository = root.GetProperty("repository");
        repository.GetProperty("id").GetString().ShouldBe("repo-001");
        repository.GetProperty("root").GetString().ShouldBe("/workspace/repository");
        repository.GetProperty("resolvedRoot").GetString().ShouldBe("/workspace/repository");
        repository.GetProperty("rootPlatformIdentity").GetString().ShouldBe("device:42;inode:1");
        repository.GetProperty("baseRevision").GetString().ShouldBe(new string('a', 40));
        repository.GetProperty("sourceFingerprint").GetString().ShouldBe(new string('b', 64));

        var operation = root.GetProperty("operations")[0];
        operation.GetProperty("sequence").GetInt32().ShouldBe(1);
        operation.GetProperty("name").GetString().ShouldBe("rename");
        operation.GetProperty("stableInput").GetString().ShouldBe("Example.Before");
        operation.GetProperty("arguments")[0].GetProperty("name").GetString().ShouldBe("kind");
        operation.GetProperty("arguments")[0].GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);

        var dependency = root.GetProperty("dependencies")[0];
        dependency.GetProperty("path").GetString().ShouldBe("src");
        dependency.GetProperty("resolvedPath").GetString().ShouldBe("/workspace/repository/src");
        dependency.GetProperty("kind").GetString().ShouldBe("directoryEntry");
        dependency.GetProperty("expectedState").GetProperty("kind").GetString().ShouldBe("directory");
        dependency.GetProperty("guard").GetProperty("required").GetBoolean().ShouldBeTrue();

        var effect = root.GetProperty("effects")[0];
        effect.GetProperty("kind").GetString().ShouldBe("replace");
        effect.GetProperty("artifactKind").GetString().ShouldBe("roslynDocument");
        effect.GetProperty("destinationPath").ValueKind.ShouldBe(JsonValueKind.Null);
        effect.GetProperty("sourceBefore").GetProperty("kind").GetString().ShouldBe("regularFile");
        effect.GetProperty("originalContentBase64").GetString().ShouldBe(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("class Before;\n")));
        effect.GetProperty("replacementContentBase64").GetString().ShouldBe(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("class After;\n")));
        effect.GetProperty("guards")[0].GetProperty("required").GetBoolean().ShouldBeTrue();

        root.GetProperty("affectedPaths")[0].GetString().ShouldBe("src/Feature.cs");
        root.GetProperty("diagnostics")[0].GetProperty("severity").GetString().ShouldBe("information");
        root.GetProperty("confidence").GetProperty("level").GetString().ShouldBe("Proof");
        root.GetProperty("workspace").GetProperty("state").GetString().ShouldBe("complete");
        root.GetProperty("verificationActions")[0].GetProperty("required").GetBoolean().ShouldBeTrue();
        root.GetProperty("coordination").GetProperty("domain").GetString().ShouldBe("exclusivePrincipal");
        root.GetProperty("coordination").GetProperty("mode").GetString().ShouldBe("externalWriteGuards");
        root.GetProperty("pathPolicy").GetProperty("caseSensitivity").GetString().ShouldBe("sensitive");
        root.GetProperty("pathPolicy").GetProperty("longPath").GetString().ShouldBe("reject");
        root.GetProperty("pathPolicy").GetProperty("readOnly").GetString().ShouldBe("reject");
        root.GetProperty("pathPolicy").GetProperty("symlink").GetString().ShouldBe("reject");
        root.GetProperty("pathPolicy").GetProperty("hardLink").GetString().ShouldBe("reject");
    }

    [Fact]
    public void hash_and_envelope_apis_reject_null_or_malformed_inputs_exactly()
    {
        Should.Throw<ArgumentNullException>(() => CanonicalChangePlan.Serialize(null!)).ParamName.ShouldBe("payload");
        Should.Throw<ArgumentNullException>(() => CanonicalChangePlan.VerifyHash(null!, new string('0', 64)))
            .ParamName.ShouldBe("payload");
        Should.Throw<ArgumentNullException>(() => CanonicalChangePlan.VerifyHash(null!, "bad"))
            .ParamName.ShouldBe("payload");
        Should.Throw<ArgumentNullException>(() => ChangePlanEnvelope.Create(null!)).ParamName.ShouldBe("payload");
        Should.Throw<ArgumentNullException>(() => ChangePlanEnvelope.FromStored(null!, "hash", []))
            .ParamName.ShouldBe("payload");
        Should.Throw<ArgumentNullException>(() => ChangePlanEnvelope.FromStored(CreatePayload(), null!, []))
            .ParamName.ShouldBe("claimedHash");
        Should.Throw<ArgumentNullException>(() => ChangePlanEnvelope.FromStored(CreatePayload(), "hash", null!))
            .ParamName.ShouldBe("canonicalPayloadBytes");
        Should.Throw<ArgumentNullException>(() => CanonicalChangePlan.ComputeHash(null!)).ParamName.ShouldBe("payload");

        var payload = CreatePayload();
        foreach (var claimedHash in new[]
                 {
                     null,
                     string.Empty,
                     new string('0', 63),
                     new string('0', 65),
                     "g" + new string('0', 63),
                     new string('0', 63) + "g",
                     "A" + new string('0', 63)
                 })
        {
            CanonicalChangePlan.VerifyHash(payload, claimedHash!).ShouldBeFalse();
        }

        var validHash = CanonicalChangePlan.ComputeHash(payload);
        var highF = Enumerable.Range(0, validHash.Length)
            .First(index => index % 2 == 0 && validHash[index] == 'f');
        var invalidNibble = validHash.ToCharArray();
        invalidNibble[highF] = 'g';
        CanonicalChangePlan.VerifyHash(payload, new string(invalidNibble)).ShouldBeFalse();
    }

    [Fact]
    public void path_state_rejects_every_invalid_missing_or_existing_shape()
    {
        Should.Throw<ArgumentNullException>(() => new ChangePlanPathState(
            false, ChangePlanPathKind.Missing, null, null, null, null!, null, null, null, null, null, null, null))
            .ParamName.ShouldBe("knownHardLinkAliases");

        var invalidMissingStates = new Func<ChangePlanPathState>[]
        {
            () => RawState(false, ChangePlanPathKind.RegularFile),
            () => RawState(false, ChangePlanPathKind.Missing, contentHash: new string('a', 64)),
            () => RawState(false, ChangePlanPathKind.Missing, platformFileIdentity: "device:1;inode:2"),
            () => RawState(false, ChangePlanPathKind.Missing, linkCount: 1),
            () => RawState(false, ChangePlanPathKind.Missing, knownHardLinkAliases: ["src/A.cs"]),
            () => RawState(false, ChangePlanPathKind.Missing, encodingName: "utf-8"),
            () => RawState(false, ChangePlanPathKind.Missing, hasByteOrderMark: false),
            () => RawState(false, ChangePlanPathKind.Missing, newline: "none"),
            () => RawState(false, ChangePlanPathKind.Missing, fileMode: "0644"),
            () => RawState(false, ChangePlanPathKind.Missing, executable: false),
            () => RawState(false, ChangePlanPathKind.Missing, readOnly: false),
            () => RawState(false, ChangePlanPathKind.Missing, symlinkTarget: "target")
        };
        foreach (var create in invalidMissingStates)
        {
            var exception = Should.Throw<ArgumentException>(() => _ = create());
            exception.ParamName.ShouldBe("exists");
            exception.Message.ShouldStartWith("A missing path state cannot carry file metadata.");
        }

        Should.Throw<ArgumentException>(() => RawState(true, ChangePlanPathKind.Missing))
            .Message.ShouldStartWith("An existing path cannot have kind Missing.");
        Should.Throw<ArgumentException>(() => RawState(true, ChangePlanPathKind.RegularFile))
            .Message.ShouldStartWith("An existing regular file requires a content hash.");
        Should.Throw<ArgumentOutOfRangeException>(() => RawState(
            true, ChangePlanPathKind.Directory, linkCount: 0))
            .Message.ShouldStartWith("Link count must be positive when known.");
        Should.Throw<ArgumentException>(() => RawState(
            true, ChangePlanPathKind.SymbolicLink, linkCount: 1))
            .Message.ShouldStartWith("A symbolic link requires its exact link target.");
        Should.Throw<ArgumentException>(() => RawState(
            true, ChangePlanPathKind.Directory, linkCount: 1, symlinkTarget: "target"))
            .Message.ShouldStartWith("Only a symbolic link can declare a link target.");
    }

    [Fact]
    public void dependency_requires_matching_guard_existing_state_and_compatible_kind()
    {
        var guard = ExternalGuard("src/input.cs");
        var file = ExistingFile("input\n", platformFileIdentity: "device:42;inode:90");

        Should.Throw<ArgumentNullException>(() => new ChangePlanDependency(
            "src/input.cs", "/workspace/repository/src/input.cs", ChangePlanDependencyKind.SemanticInput, null!, guard))
            .ParamName.ShouldBe("expectedState");
        Should.Throw<ArgumentNullException>(() => new ChangePlanDependency(
            "src/input.cs", "/workspace/repository/src/input.cs", ChangePlanDependencyKind.SemanticInput, file, null!))
            .ParamName.ShouldBe("guard");
        Should.Throw<ArgumentException>(() => new ChangePlanDependency(
            "src/input.cs", "/workspace/repository/src/input.cs", ChangePlanDependencyKind.SemanticInput,
            file, ExternalGuard("src/other.cs")))
            .Message.ShouldStartWith("A dependency guard must identify the dependency path.");
        Should.Throw<ArgumentException>(() => new ChangePlanDependency(
            "src/input.cs", "/workspace/repository/src/input.cs", ChangePlanDependencyKind.SemanticInput,
            ChangePlanPathState.Missing, guard))
            .Message.ShouldStartWith("A reviewed read dependency must exist.");
        Should.Throw<ArgumentException>(() => new ChangePlanDependency(
            "src/input.cs", "/workspace/repository/src/input.cs", ChangePlanDependencyKind.DirectoryEntry,
            file, guard))
            .Message.ShouldStartWith("A directory-entry dependency requires a directory;");
        Should.Throw<ArgumentException>(() => new ChangePlanDependency(
            "src", "/workspace/repository/src", ChangePlanDependencyKind.ProjectInput,
            DirectoryState("device:42;inode:91"), ExternalGuard("src")))
            .Message.ShouldStartWith("A directory-entry dependency requires a directory;");
    }

    [Fact]
    public void effect_constructor_rejects_invalid_sequences_nulls_and_guard_sets()
    {
        var original = Encoding.UTF8.GetBytes("class Before;\n");
        var replacement = Encoding.UTF8.GetBytes("class After;\n");
        var before = ExistingFile(original);
        var after = ExistingFile(replacement);

        Should.Throw<ArgumentOutOfRangeException>(() => NewEffect(
            0, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, after, null, null, original, replacement, [ExternalGuard("src/Feature.cs")]))
            .ParamName.ShouldBe("sequence");
        Should.Throw<ArgumentNullException>(() => NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, null!, after, null, null, original, replacement, [ExternalGuard("src/Feature.cs")]))
            .ParamName.ShouldBe("sourceBefore");
        Should.Throw<ArgumentNullException>(() => NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, null!, null, null, original, replacement, [ExternalGuard("src/Feature.cs")]))
            .ParamName.ShouldBe("sourceAfter");
        Should.Throw<ArgumentNullException>(() => NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, after, null, null, original, replacement, null!))
            .ParamName.ShouldBe("guards");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, after, null, null, original, replacement, [null!]))
            .Message.ShouldStartWith("Guard requirements cannot contain null values.");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, after, null, null, original, replacement,
            [ExternalGuard("src/Feature.cs"), ExternalGuard("src/Feature.cs")]))
            .Message.ShouldStartWith("An effect must declare one unique guard requirement");

        var ordered = NewEffect(
            1,
            ChangePlanEffectKind.Move,
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            "src/Moved.cs",
            "/workspace/repository/src/Moved.cs",
            before,
            ChangePlanPathState.Missing,
            ChangePlanPathState.Missing,
            ExistingFile(original, platformFileIdentity: null),
            original,
            null,
            [ExternalGuard("src/Moved.cs"), ExternalGuard("src/Feature.cs")]);
        ordered.Guards.Select(item => item.Path).ShouldBe(["src/Feature.cs", "src/Moved.cs"]);
    }

    [Fact]
    public void create_replace_and_delete_effects_enforce_each_shape_and_content_precondition()
    {
        var original = Encoding.UTF8.GetBytes("class Before;\n");
        var replacement = Encoding.UTF8.GetBytes("class After;\n");
        var before = ExistingFile(original);
        var after = ExistingFile(replacement);
        var guard = ExternalGuard("src/Feature.cs");

        var destinationViolations = new Func<ChangePlanEffect>[]
        {
            () => NewEffect(1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
                "src/Other.cs", null, before, after, null, null, original, replacement, [guard, ExternalGuard("src/Other.cs")]),
            () => NewEffect(1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
                null, "/workspace/repository/src/Other.cs", before, after, null, null, original, replacement, [guard]),
            () => NewEffect(1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
                null, null, before, after, ChangePlanPathState.Missing, null, original, replacement, [guard]),
            () => NewEffect(1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
                null, null, before, after, null, ChangePlanPathState.Missing, original, replacement, [guard])
        };
        foreach (var create in destinationViolations)
            Should.Throw<ArgumentException>(() => _ = create()).Message.ShouldContain("cannot declare a destination");

        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Create, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, ChangePlanPathState.Missing, after, null, null, original, replacement, [guard]))
            .Message.ShouldStartWith("A create effect cannot contain original content.");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Create, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, ChangePlanPathState.Missing, after, null, null, null, null, [guard]))
            .Message.ShouldStartWith("A create or replace effect requires exact replacement content.");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Create, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, after, null, null, null, replacement, [guard]))
            .Message.ShouldContain("before/after states");

        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, ChangePlanPathState.Missing, after, null, null, original, replacement, [guard]))
            .Message.ShouldContain("before/after states");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, ChangePlanPathState.Missing, null, null, original, replacement, [guard]))
            .Message.ShouldContain("before/after states");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, after, null, null, null, replacement, [guard]))
            .Message.ShouldStartWith("An existing source requires exact original content.");

        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Create, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Other.cs", "/workspace/repository/src/Other.cs",
            ChangePlanPathState.Missing, after, ChangePlanPathState.Missing, after,
            null, replacement, [guard, ExternalGuard("src/Other.cs")]))
            .Message.ShouldContain("cannot declare a destination");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Delete, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Other.cs", "/workspace/repository/src/Other.cs",
            before, ChangePlanPathState.Missing, ChangePlanPathState.Missing, after,
            original, null, [guard, ExternalGuard("src/Other.cs")]))
            .Message.ShouldContain("cannot declare a destination");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, after, null, null, Encoding.UTF8.GetBytes("wrong\n"), replacement, [guard]))
            .Message.ShouldStartWith("Replacement content does not match the declared postcondition hash.");

        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Delete, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, ChangePlanPathState.Missing, null, null, original, replacement, [guard]))
            .Message.ShouldStartWith("A delete effect cannot contain replacement content.");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Delete, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, after, null, null, original, null, [guard]))
            .Message.ShouldContain("before/after states");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Delete, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, ChangePlanPathState.Missing, null, null, null, null, [guard]))
            .Message.ShouldStartWith("An existing source requires exact original content.");
    }

    [Fact]
    public void move_effect_enforces_complete_distinct_destination_and_content_binding()
    {
        var original = Encoding.UTF8.GetBytes("class Before;\n");
        var replacement = Encoding.UTF8.GetBytes("class Rewritten;\n");
        var before = ExistingFile(original);
        var destination = ExistingFile(original, platformFileIdentity: null);
        var guards = new[] { ExternalGuard("src/Feature.cs"), ExternalGuard("src/Moved.cs") };

        var incompleteMoves = new Func<ChangePlanEffect>[]
        {
            () => NewEffect(1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
                null, "/workspace/repository/src/Moved.cs", before, ChangePlanPathState.Missing,
                ChangePlanPathState.Missing, destination, original, null, [ExternalGuard("src/Feature.cs")]),
            () => NewEffect(1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
                "src/Moved.cs", null, before, ChangePlanPathState.Missing,
                ChangePlanPathState.Missing, destination, original, null, guards),
            () => NewEffect(1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
                "src/Moved.cs", "/workspace/repository/src/Moved.cs", before, ChangePlanPathState.Missing,
                null, destination, original, null, guards),
            () => NewEffect(1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
                "src/Moved.cs", "/workspace/repository/src/Moved.cs", before, ChangePlanPathState.Missing,
                ChangePlanPathState.Missing, null, original, null, guards)
        };
        foreach (var create in incompleteMoves)
            Should.Throw<ArgumentException>(() => _ = create()).Message.ShouldStartWith("A move effect requires a destination");

        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Feature.cs", "/workspace/repository/src/Feature.cs", before, ChangePlanPathState.Missing,
            ChangePlanPathState.Missing, destination, original, null, [ExternalGuard("src/Feature.cs")]))
            .Message.ShouldStartWith("A move source and destination must be different.");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs", before, before,
            ChangePlanPathState.Missing, destination, original, null, guards))
            .Message.ShouldContain("before/after states");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs", before, ChangePlanPathState.Missing,
            before, destination, original, null, guards))
            .Message.ShouldStartWith("A move requires an absent destination before");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs", before, ChangePlanPathState.Missing,
            ChangePlanPathState.Missing, ChangePlanPathState.Missing, original, null, guards))
            .Message.ShouldStartWith("A move requires an absent destination before");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs", before, ChangePlanPathState.Missing,
            ChangePlanPathState.Missing, ExistingFile("different\n", platformFileIdentity: null), original, null, guards))
            .Message.ShouldStartWith("A content-preserving move requires matching source and destination hashes.");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs", before, ChangePlanPathState.Missing,
            ChangePlanPathState.Missing, ExistingFile("different\n", platformFileIdentity: null), original, replacement, guards))
            .Message.ShouldStartWith("Replacement content does not match the declared postcondition hash.");
        Should.Throw<ArgumentException>(() => NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs", before, ChangePlanPathState.Missing,
            ChangePlanPathState.Missing, destination, null, null, guards))
            .Message.ShouldStartWith("An existing source requires exact original content.");
        Should.Throw<InvalidOperationException>(() => NewEffect(
            1, (ChangePlanEffectKind)99, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, before, before, null, null, original, original, [ExternalGuard("src/Feature.cs")]))
            .Message.ShouldStartWith("Unsupported change-plan effect kind");
    }

    [Fact]
    public void payload_requires_nonempty_unique_sequenced_work_and_required_verification()
    {
        Should.Throw<ArgumentException>(() => CreatePayload(operations: []))
            .Message.ShouldStartWith("A change plan requires at least one canonical operation.");
        Should.Throw<ArgumentException>(() => CreatePayload(effects: []))
            .Message.ShouldStartWith("A change plan requires at least one effect.");
        Should.Throw<ArgumentException>(() => CreatePayload(
            expiresAtUtc: new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero)))
            .Message.ShouldStartWith("Plan expiry must be after creation.");

        var operation = new ChangePlanOperation(1, "rename", "Example.Before", []);
        Should.Throw<ArgumentException>(() => CreatePayload(operations: [operation, operation]))
            .Message.ShouldStartWith("Change-plan operations must have unique sequence values.");
        var effect = CreateReplacementEffect("src/Feature.cs", Encoding.UTF8.GetBytes("class After;\n"));
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [effect, effect]))
            .Message.ShouldStartWith("Change-plan effects must have unique sequence values.");

        Should.Throw<ArgumentException>(() => CreatePayload(verificationActions: []))
            .Message.ShouldStartWith("A change plan requires at least one required verification action.");
        var optional = new ChangePlanVerificationAction(1, "optional", "inspect", "reviewed", required: false);
        Should.Throw<ArgumentException>(() => CreatePayload(verificationActions: [optional]))
            .Message.ShouldStartWith("A change plan requires at least one required verification action.");
        var required = new ChangePlanVerificationAction(2, "required", "compile", "passes", required: true);
        Should.NotThrow(() => CreatePayload(verificationActions: [optional, required]));
        Should.Throw<ArgumentException>(() => CreatePayload(verificationActions: [required, required]))
            .Message.ShouldStartWith("Change-plan verification actions must have unique sequence values.");
    }

    [Fact]
    public void payload_rejects_unknown_cleanup_and_recovery_semantics_before_hashing()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CreatePayload(
            cleanupPolicy: (ChangePlanCleanupPolicy)99))
            .ParamName.ShouldBe("cleanupPolicy");
        Should.Throw<ArgumentOutOfRangeException>(() => CreatePayload(
            recoveryPolicy: (ChangePlanRecoveryPolicy)99))
            .ParamName.ShouldBe("recoveryPolicy");
    }

    [Fact]
    public void sequence_models_reject_zero_and_payload_orders_all_canonical_collections()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ChangePlanOperation(0, "rename", "input", []))
            .ParamName.ShouldBe("sequence");
        Should.Throw<ArgumentOutOfRangeException>(() => new ChangePlanVerificationAction(
            0, "compile", "dotnet build", "passes", required: true))
            .ParamName.ShouldBe("sequence");
        Should.Throw<ArgumentException>(() => new ChangePlanConfidence("Proof", "basis", []))
            .Message.ShouldStartWith("Plan confidence requires affirmative evidence.");
        Should.Throw<ArgumentException>(() => new ChangePlanWorkspace(
            WorkspaceCompletenessState.Complete,
            "/workspace/repository/RoslynTools.slnx",
            "Debug",
            [],
            "sdk",
            "compiler",
            "mode",
            "trusted"))
            .Message.ShouldStartWith("Workspace provenance requires at least one target framework.");

        var operations = new[]
        {
            new ChangePlanOperation(2, "second", "B", []),
            new ChangePlanOperation(1, "first", "A", [])
        };
        var effects = new[]
        {
            CreateReplacementEffect("src/Z.cs", Encoding.UTF8.GetBytes("class Z;\n"), 2,
                sourceBefore: ExistingFile("class Before;\n", "device:42;inode:101")),
            CreateReplacementEffect("src/A.cs", Encoding.UTF8.GetBytes("class A;\n"), 1,
                sourceBefore: ExistingFile("class Before;\n", "device:42;inode:100"))
        };
        var dependencies = new[]
        {
            CreateDirectoryDependency("z", linkCount: 2),
            new ChangePlanDependency(
                "a",
                "/workspace/repository/a",
                ChangePlanDependencyKind.DirectoryEntry,
                DirectoryState("device:42;inode:102", 2),
                ExternalGuard("a"))
        };
        var actions = new[]
        {
            new ChangePlanVerificationAction(2, "second", "inspect", "reviewed", required: false),
            new ChangePlanVerificationAction(1, "first", "compile", "passes", required: true)
        };
        var payload = CreatePayload(
            operations: operations,
            effects: effects,
            dependencies: dependencies,
            assumptions: ["Z", "A"],
            limitations: ["Z", "A"],
            verificationActions: actions);

        payload.Operations.Select(item => item.Sequence).ShouldBe([1, 2]);
        payload.Effects.Select(item => item.Sequence).ShouldBe([1, 2]);
        payload.Dependencies.Select(item => item.Path).ShouldBe(["a", "z"]);
        payload.AffectedPaths.ShouldBe(["src/A.cs", "src/Z.cs"]);
        payload.Assumptions.ShouldBe(["A", "Z"]);
        payload.Limitations.ShouldBe(["A", "Z"]);
        payload.VerificationActions.Select(item => item.Sequence).ShouldBe([1, 2]);
    }

    [Fact]
    public void payload_rejects_null_entries_duplicates_and_path_collisions()
    {
        Should.Throw<ArgumentException>(() => CreatePayload(operations: [null!]))
            .Message.ShouldStartWith("Change-plan operations cannot contain null values.");
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [null!]))
            .Message.ShouldStartWith("Change-plan effects cannot contain null values.");
        Should.Throw<ArgumentException>(() => CreatePayload(diagnostics: [null!]))
            .Message.ShouldStartWith("Change-plan diagnostics cannot contain null values.");
        Should.Throw<ArgumentException>(() => CreatePayload(verificationActions: [null!]))
            .Message.ShouldStartWith("Change-plan verification actions cannot contain null values.");
        Should.Throw<ArgumentException>(() => CreatePayload(assumptions: ["same", "same"]))
            .Message.ShouldStartWith("Change-plan values must be unique.");
        Should.Throw<ArgumentException>(() => CreatePayload(limitations: ["same", "same"]))
            .Message.ShouldStartWith("Change-plan values must be unique.");

        var affectedDependency = new ChangePlanDependency(
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            ChangePlanDependencyKind.SemanticInput,
            ExistingFile("class Before;\n", "device:42;inode:200"),
            ExternalGuard("src/Feature.cs"));
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [affectedDependency]))
            .Message.ShouldStartWith("Read dependencies must be distinct from affected paths.");

        var upper = CreateReplacementEffect("src/Feature.cs", Encoding.UTF8.GetBytes("class After;\n"));
        var lower = CreateReplacementEffect(
            "SRC/FEATURE.CS",
            Encoding.UTF8.GetBytes("class Other;\n"),
            2,
            sourceBefore: ExistingFile("class Before;\n", "device:42;inode:201"));
        Should.Throw<ArgumentException>(() => CreatePayload(
            effects: [upper, lower],
            pathPolicy: RejectPathPolicy(ChangePlanCaseSensitivity.Insensitive)))
            .Message.ShouldContain("more than one effect");
    }

    [Fact]
    public void diagnostic_comparer_orders_every_field_and_nullable_context_value()
    {
        var baseline = new ChangePlanDiagnostic(
            "code",
            MutationDiagnosticSeverity.Information,
            "message",
            [new ChangePlanValue("name", null)]);

        ChangePlanDiagnosticComparer.CompareCanonical(null, null).ShouldBe(0);
        ChangePlanDiagnosticComparer.CompareCanonical(null, baseline).ShouldBeLessThan(0);
        ChangePlanDiagnosticComparer.CompareCanonical(baseline, null).ShouldBeGreaterThan(0);
        ChangePlanDiagnosticComparer.CompareCanonical(baseline, baseline).ShouldBe(0);
        ChangePlanDiagnosticComparer.CompareCanonical(
            baseline,
            new ChangePlanDiagnostic("other", baseline.Severity, baseline.Message, baseline.Context))
            .ShouldBeLessThan(0);
        ChangePlanDiagnosticComparer.CompareCanonical(
            baseline,
            new ChangePlanDiagnostic("code", MutationDiagnosticSeverity.Warning, baseline.Message, baseline.Context))
            .ShouldBeLessThan(0);
        ChangePlanDiagnosticComparer.CompareCanonical(
            baseline,
            new ChangePlanDiagnostic("code", baseline.Severity, "other", baseline.Context))
            .ShouldBeLessThan(0);
        ChangePlanDiagnosticComparer.CompareCanonical(
            baseline,
            new ChangePlanDiagnostic("code", baseline.Severity, baseline.Message, [new ChangePlanValue("other", null)]))
            .ShouldBeLessThan(0);
        ChangePlanDiagnosticComparer.CompareCanonical(
            baseline,
            new ChangePlanDiagnostic("code", baseline.Severity, baseline.Message, [new ChangePlanValue("name", "value")]))
            .ShouldBeLessThan(0);
        ChangePlanDiagnosticComparer.CompareCanonical(
            new ChangePlanDiagnostic("code", baseline.Severity, baseline.Message, [new ChangePlanValue("name", "value")]),
            baseline)
            .ShouldBeGreaterThan(0);
        ChangePlanDiagnosticComparer.CompareCanonical(
            baseline,
            new ChangePlanDiagnostic(
                "code",
                baseline.Severity,
                baseline.Message,
                [new ChangePlanValue("name", null), new ChangePlanValue("second", "value")]))
            .ShouldBeLessThan(0);
        ChangePlanDiagnosticComparer.CompareCanonical(
            baseline,
            new ChangePlanDiagnostic("code", baseline.Severity, baseline.Message, [new ChangePlanValue("name", null)]))
            .ShouldBe(0);

        var ordered = CreatePayload(diagnostics:
        [
            new ChangePlanDiagnostic("z", MutationDiagnosticSeverity.Error, "last", []),
            new ChangePlanDiagnostic("a", MutationDiagnosticSeverity.Information, "first", [])
        ]);
        ordered.Diagnostics.Select(item => item.Code).ShouldBe(["a", "z"]);
        Should.Throw<ArgumentException>(() => CreatePayload(diagnostics:
        [
            new ChangePlanDiagnostic("a", MutationDiagnosticSeverity.Information, "first", []),
            null!
        ])).Message.ShouldStartWith("Change-plan diagnostics cannot contain null values.");
    }

    [Fact]
    public void roots_must_be_unsymlinked_distinct_contained_and_identity_separated()
    {
        Should.Throw<ArgumentException>(() => CreatePayload(
            repository: CreateRepository(resolvedRoot: "/workspace/repository-alias")))
            .Message.ShouldContain("repository root resolved path");
        Should.Throw<ArgumentException>(() => CreatePayload(
            coordination: CreateCoordination(stagingRootResolvedPath: "/var/lib/roslyn-tools/staging-alias")))
            .Message.ShouldContain("staging root resolved path");
        Should.Throw<ArgumentException>(() => CreatePayload(
            coordination: CreateCoordination(backupRootResolvedPath: "/var/lib/roslyn-tools/backup-alias")))
            .Message.ShouldContain("backup root resolved path");

        Should.Throw<ArgumentException>(() => CreatePayload(
            coordination: CreateCoordination(stagingRoot: "/var/lib/elsewhere/staging")))
            .Message.ShouldStartWith("Staging and backup roots must be distinct children");
        Should.Throw<ArgumentException>(() => CreatePayload(
            coordination: CreateCoordination(backupRoot: "/var/lib/elsewhere/backup")))
            .Message.ShouldStartWith("Staging and backup roots must be distinct children");

        const string control = "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555";
        Should.Throw<ArgumentException>(() => CreatePayload(
            coordination: CreateCoordination(stagingRoot: control)))
            .Message.ShouldStartWith("Staging and backup roots must be distinct children");
        Should.Throw<ArgumentException>(() => CreatePayload(
            coordination: CreateCoordination(backupRoot: control)))
            .Message.ShouldStartWith("Staging and backup roots must be distinct children");
        Should.Throw<ArgumentException>(() => CreatePayload(
            coordination: CreateCoordination(backupRoot:
                "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555/staging")))
            .Message.ShouldStartWith("Staging and backup roots must be distinct children");

        const string repositoryControl = "/workspace/repository/.rt-control";
        Should.Throw<ArgumentException>(() => CreatePayload(coordination: CreateCoordination(
            controlRoot: repositoryControl,
            stagingRoot: repositoryControl + "/staging",
            backupRoot: repositoryControl + "/backup")))
            .Message.ShouldStartWith("Protected roots and the repository root must not overlap");
        Should.Throw<ArgumentException>(() => CreatePayload(coordination: CreateCoordination(
            controlRootPlatformIdentity: "device:42;inode:1")))
            .Message.ShouldStartWith("Protected roots and the repository root must not overlap");
        Should.Throw<ArgumentException>(() => CreatePayload(coordination: CreateCoordination(
            stagingRootPlatformIdentity: "device:99;inode:1")))
            .Message.ShouldStartWith("Control, staging, and backup roots require distinct platform identities.");

        const string defaultControl = "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555";
        const string defaultStaging = defaultControl + "/staging";
        Should.Throw<ArgumentException>(() => CreatePayload(
            repository: CreateRepository(root: defaultStaging + "/repository")))
            .Message.ShouldStartWith("Protected roots and the repository root must not overlap");
    }

    [Fact]
    public void resolved_paths_dependencies_and_platform_identities_are_bound_to_the_reviewed_tree()
    {
        var dependency = new ChangePlanDependency(
            "src",
            "/workspace/elsewhere/src",
            ChangePlanDependencyKind.DirectoryEntry,
            DirectoryState("device:42;inode:2"),
            ExternalGuard("src"));
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [dependency]))
            .Message.ShouldContain("unsymlinked repository path");

        var containedAlias = new ChangePlanDependency(
            "src",
            "/workspace/repository/other-src",
            ChangePlanDependencyKind.DirectoryEntry,
            DirectoryState("device:42;inode:2"),
            ExternalGuard("src"));
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [containedAlias]))
            .Message.ShouldContain("unsymlinked repository path");

        var original = Encoding.UTF8.GetBytes("class Before;\n");
        var move = NewEffect(
            1,
            ChangePlanEffectKind.Move,
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            "src/Moved.cs",
            "/workspace/elsewhere/Moved.cs",
            ExistingFile(original),
            ChangePlanPathState.Missing,
            ChangePlanPathState.Missing,
            ExistingFile(original, platformFileIdentity: null),
            original,
            null,
            [ExternalGuard("src/Feature.cs"), ExternalGuard("src/Moved.cs")]);
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [move]))
            .Message.ShouldContain("unsymlinked repository path");

        var rootAliased = CreateReplacementEffect(
            "src/Feature.cs",
            Encoding.UTF8.GetBytes("class After;\n"),
            sourceBefore: ExistingFile("class Before;\n", "device:42;inode:1"));
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [rootAliased]))
            .Message.ShouldStartWith("A repository-relative plan path cannot share the repository root platform identity.");

        var aliasedDependency = new ChangePlanDependency(
            "inputs/config.json",
            "/workspace/repository/inputs/config.json",
            ChangePlanDependencyKind.ProjectInput,
            ExistingFile("{}\n", "device:42;inode:84"),
            ExternalGuard("inputs/config.json"));
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [aliasedDependency]))
            .Message.ShouldStartWith("Distinct plan paths cannot share one platform file identity.");

        var unaffectedDependency = CreateDirectoryDependency("inputs", linkCount: 2);
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [unaffectedDependency, new ChangePlanDependency(
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            ChangePlanDependencyKind.SemanticInput,
            ExistingFile("class Before;\n", "device:42;inode:202"),
            ExternalGuard("src/Feature.cs"))]))
            .Message.ShouldStartWith("Read dependencies must be distinct from affected paths.");

        var missingIdentityDependency = new ChangePlanDependency(
            "inputs/config.json",
            "/workspace/repository/inputs/config.json",
            ChangePlanDependencyKind.ProjectInput,
            ExistingFile("{}\n", platformFileIdentity: null),
            ExternalGuard("inputs/config.json"));
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [missingIdentityDependency]))
            .Message.ShouldStartWith("Every existing affected path requires platform identity");
    }

    [Fact]
    public void every_v1_path_policy_is_fail_closed()
    {
        var invalidPolicies = new[]
        {
            new ChangePlanPathPolicy(ChangePlanCaseSensitivity.Sensitive, ChangePlanLongPathPolicy.Allow,
                ChangePlanReadOnlyPolicy.Reject, ChangePlanSymlinkPolicy.Reject, ChangePlanHardLinkPolicy.Reject),
            new ChangePlanPathPolicy(ChangePlanCaseSensitivity.Sensitive, ChangePlanLongPathPolicy.Reject,
                ChangePlanReadOnlyPolicy.Preserve, ChangePlanSymlinkPolicy.Reject, ChangePlanHardLinkPolicy.Reject),
            new ChangePlanPathPolicy(ChangePlanCaseSensitivity.Sensitive, ChangePlanLongPathPolicy.Reject,
                ChangePlanReadOnlyPolicy.Replace, ChangePlanSymlinkPolicy.Reject, ChangePlanHardLinkPolicy.Reject),
            new ChangePlanPathPolicy(ChangePlanCaseSensitivity.Sensitive, ChangePlanLongPathPolicy.Reject,
                ChangePlanReadOnlyPolicy.Reject, ChangePlanSymlinkPolicy.PreserveLink, ChangePlanHardLinkPolicy.Reject),
            new ChangePlanPathPolicy(ChangePlanCaseSensitivity.Sensitive, ChangePlanLongPathPolicy.Reject,
                ChangePlanReadOnlyPolicy.Reject, ChangePlanSymlinkPolicy.FollowTarget, ChangePlanHardLinkPolicy.Reject),
            new ChangePlanPathPolicy(ChangePlanCaseSensitivity.Sensitive, ChangePlanLongPathPolicy.Reject,
                ChangePlanReadOnlyPolicy.Reject, ChangePlanSymlinkPolicy.Reject, ChangePlanHardLinkPolicy.Preserve),
            new ChangePlanPathPolicy(ChangePlanCaseSensitivity.Sensitive, ChangePlanLongPathPolicy.Reject,
                ChangePlanReadOnlyPolicy.Reject, ChangePlanSymlinkPolicy.Reject, ChangePlanHardLinkPolicy.BreakByReplacement)
        };

        foreach (var policy in invalidPolicies)
        {
            Should.Throw<ArgumentException>(() => CreatePayload(pathPolicy: policy))
                .Message.ShouldStartWith("rt.change-plan.v1 currently requires reject policies");
        }
    }

    [Fact]
    public void repository_paths_and_hashes_are_normalized_or_rejected_at_the_boundary()
    {
        ChangePlanValidation.NormalizeRepositoryPath("./src\\Feature.cs", "path")
            .ShouldBe("src/Feature.cs");
        ChangePlanValidation.NormalizeSha256(new string('A', 64), "hash")
            .ShouldBe(new string('a', 64));
        ChangePlanValidation.NormalizeRepositoryPath(new string('a', 255), "path")
            .ShouldBe(new string('a', 255));

        foreach (var path in new[]
                 {
                     string.Empty,
                     "/absolute.cs",
                     "C:/absolute.cs",
                     "../outside.cs",
                     ".",
                     "src/CON.txt",
                     "src/name.",
                     "src/name ",
                     "src/name:stream",
                     "src/name?.cs",
                     "src/" + new string('a', 256)
                 })
        {
            Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeRepositoryPath(path, "path"));
        }

        Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeAbsolutePath("relative/path", "path"))
            .Message.ShouldStartWith("Path must be absolute.");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeSha256(new string('a', 63), "hash"))
            .Message.ShouldStartWith("Value must be a 64-character SHA-256 hexadecimal digest.");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeSha256(string.Empty, "hash"))
            .Message.ShouldStartWith("Value cannot be empty or whitespace.");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeRepositoryPath("C:", "path"))
            .Message.ShouldStartWith("Path must be repository-relative.");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeRepositoryPath("src/<name>.cs", "path"))
            .Message.ShouldContain("non-portable");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeRepositoryPath("src/name\u0001.cs", "path"))
            .Message.ShouldContain("non-portable");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeAbsolutePath(string.Empty, "path"))
            .Message.ShouldStartWith("Value cannot be empty or whitespace.");

        foreach (var reserved in new[]
                 {
                     "CON", "PRN", "AUX", "NUL",
                     "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                     "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
                     "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"
                 })
        {
            Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeRepositoryPath(
                $"src/{reserved}.txt", "path"))
                .Message.ShouldContain("non-portable");
        }
        Should.Throw<ArgumentException>(() => ChangePlanValidation.NormalizeSha256(
            "g" + new string('a', 63), "hash"))
            .Message.ShouldStartWith("Value must be a 64-character SHA-256 hexadecimal digest.");

        ChangePlanValidation.IsContained("/root", "/root", StringComparison.Ordinal).ShouldBeTrue();
        ChangePlanValidation.IsContained("/root/child", "/root", StringComparison.Ordinal).ShouldBeTrue();
        ChangePlanValidation.IsContained("/rooted", "/root", StringComparison.Ordinal).ShouldBeFalse();
        ChangePlanValidation.IsContained("/ROOT/child", "/root", StringComparison.Ordinal).ShouldBeFalse();
        ChangePlanValidation.IsContained("/ROOT/child", "/root", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    [Fact]
    public void collection_freezers_reject_nulls_and_duplicates_and_preserve_canonical_order()
    {
        Should.Throw<ArgumentNullException>(() => ChangePlanValidation.FreezeValues(null!, "values"))
            .ParamName.ShouldBe("values");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.FreezeValues([null!], "values"))
            .Message.ShouldStartWith("Change-plan values cannot contain null entries.");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.FreezeValues(
            [new ChangePlanValue("same", "1"), new ChangePlanValue("same", "2")], "values"))
            .Message.ShouldStartWith("Change-plan value names must be nonempty and unique.");
        ChangePlanValidation.FreezeValues(
            [new ChangePlanValue("z", "2"), new ChangePlanValue("a", "1")], "values")
            .Select(item => item.Name).ShouldBe(["a", "z"]);

        Should.Throw<ArgumentNullException>(() => ChangePlanValidation.FreezeStrings(null!, "values"))
            .ParamName.ShouldBe("values");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.FreezeStrings(["ok", " "], "values"))
            .Message.ShouldStartWith("Value cannot be empty or whitespace.");
        ChangePlanValidation.FreezeStrings(["z", "a"], "values").ShouldBe(["a", "z"]);

        Should.Throw<ArgumentNullException>(() => ChangePlanValidation.FreezeKnownPaths(null!, "paths"))
            .ParamName.ShouldBe("values");
        Should.Throw<ArgumentException>(() => ChangePlanValidation.FreezeKnownPaths(
            ["src/A.cs", "src/./A.cs"], "paths"))
            .Message.ShouldStartWith("Change-plan paths must be unique.");
        ChangePlanValidation.FreezeKnownPaths(
            ["src/Z.cs", "/workspace/A.cs", "src/A.cs"], "paths")
            .ShouldBe(["/workspace/A.cs", "src/A.cs", "src/Z.cs"]);
    }

    [Fact]
    public void existing_paths_require_identity_single_link_writable_and_canonical_mode_metadata()
    {
        var content = Encoding.UTF8.GetBytes("class Before;\n");
        void RejectBefore(ChangePlanPathState state, string message)
        {
            var effect = CreateReplacementEffect(
                "src/Feature.cs",
                Encoding.UTF8.GetBytes("class After;\n"),
                sourceBefore: state,
                originalContent: content);
            Should.Throw<ArgumentException>(() => CreatePayload(effects: [effect]))
                .Message.ShouldStartWith(message);
        }

        RejectBefore(ExistingFile(content, platformFileIdentity: null),
            "Every existing affected path requires platform identity");
        RejectBefore(RawState(
                true,
                ChangePlanPathKind.RegularFile,
                Hash(content),
                "device:42;inode:84",
                linkCount: null,
                encodingName: "utf-8",
                hasByteOrderMark: false,
                newline: "lf",
                fileMode: "0644",
                executable: false,
                readOnly: false),
            "Every existing affected path requires platform identity");
        RejectBefore(ExistingFile(content, knownHardLinkAliases: ["src/Alias.cs"]),
            "rt.change-plan.v1 rejects known hard-link aliases.");
        RejectBefore(ExistingFile(content, linkCount: 2),
            "rt.change-plan.v1 requires one proven link");
        RejectBefore(ExistingFile(content, readOnly: true),
            "rt.change-plan.v1 requires explicit writable path metadata.");
        RejectBefore(ExistingFile(content, readOnly: null),
            "Existing and resulting paths require canonical mode");

        foreach (var state in new[]
                 {
                     ExistingFile(content, fileMode: null),
                     ExistingFile(content, executable: null),
                     ExistingFile(content, fileMode: "644"),
                     ExistingFile(content, fileMode: "0684")
                 })
        {
            RejectBefore(state, "Existing and resulting paths require canonical mode");
        }
        RejectBefore(ExistingFile(content, fileMode: "0755", executable: false),
            "Executable metadata must agree with the canonical file mode.");
        RejectBefore(ExistingFile(content, fileMode: "0644", executable: true),
            "Executable metadata must agree with the canonical file mode.");
    }

    [Fact]
    public void directory_and_regular_file_metadata_are_distinct_and_complete()
    {
        ChangePlanDependency Dependency(ChangePlanPathState state) => new(
            "src",
            "/workspace/repository/src",
            ChangePlanDependencyKind.DirectoryEntry,
            state,
            ExternalGuard("src"));

        foreach (var state in new[]
                 {
                     RawState(true, ChangePlanPathKind.Directory, platformFileIdentity: "device:42;inode:2",
                         linkCount: 2, encodingName: "utf-8", fileMode: "0755", executable: true, readOnly: false),
                     RawState(true, ChangePlanPathKind.Directory, platformFileIdentity: "device:42;inode:2",
                         linkCount: 2, hasByteOrderMark: false, fileMode: "0755", executable: true, readOnly: false),
                     RawState(true, ChangePlanPathKind.Directory, platformFileIdentity: "device:42;inode:2",
                         linkCount: 2, newline: "none", fileMode: "0755", executable: true, readOnly: false)
                 })
        {
            Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [Dependency(state)]))
                .Message.ShouldStartWith("Directory metadata cannot contain text or content fields.");
        }

        var bytes = Encoding.UTF8.GetBytes("text\n");
        foreach (var state in new[]
                 {
                     ExistingFile(bytes, encodingName: null),
                     ExistingFile(bytes, encodingName: "UTF-8"),
                     ExistingFile(bytes, hasByteOrderMark: null),
                     ExistingFile(bytes, newline: null),
                     ExistingFile(bytes, newline: "platform")
                 })
        {
            var effect = CreateReplacementEffect(
                "src/Feature.cs",
                Encoding.UTF8.GetBytes("class After;\n"),
                sourceBefore: state,
                originalContent: bytes);
            Should.Throw<ArgumentException>(() => CreatePayload(effects: [effect]))
                .Message.ShouldStartWith("Regular files require canonical UTF-8");
        }

        var unsafeDirectory = RawState(
            true,
            ChangePlanPathKind.Directory,
            platformFileIdentity: "device:42;inode:2",
            linkCount: 2,
            knownHardLinkAliases: ["src-alias"],
            fileMode: "0755",
            executable: true,
            readOnly: false);
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [Dependency(unsafeDirectory)]))
            .Message.ShouldStartWith("rt.change-plan.v1 rejects known hard-link aliases.");

        var replacement = Encoding.UTF8.GetBytes("class After;\n");
        var invalidAfter = CreateReplacementEffect(
            "src/Feature.cs",
            replacement,
            sourceAfter: ExistingFile(replacement, newline: "none"));
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [invalidAfter]))
            .Message.ShouldStartWith("Newline metadata does not match");
    }

    [Fact]
    public void exact_utf8_bom_and_every_newline_form_are_verified_from_bytes()
    {
        var newlineCases = new[]
        {
            (Text: "text", Token: "none"),
            (Text: "text\n", Token: "lf"),
            (Text: "text\r\n", Token: "crlf"),
            (Text: "text\r", Token: "cr"),
            (Text: "a\r\nb\nc\r", Token: "mixed")
        };
        foreach (var (text, token) in newlineCases)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var effect = NewEffect(
                1,
                ChangePlanEffectKind.Replace,
                "src/Feature.cs",
                "/workspace/repository/src/Feature.cs",
                null,
                null,
                ExistingFile(bytes, newline: token),
                ExistingFile(bytes, newline: token),
                null,
                null,
                bytes,
                bytes,
                [ExternalGuard("src/Feature.cs")]);
            Should.NotThrow(() => CreatePayload(effects: [effect]));
        }

        var bomBytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("text\n")).ToArray();
        var bomEffect = NewEffect(
            1,
            ChangePlanEffectKind.Replace,
            "src/Feature.cs",
            "/workspace/repository/src/Feature.cs",
            null,
            null,
            ExistingFile(bomBytes, hasByteOrderMark: true),
            ExistingFile(bomBytes, hasByteOrderMark: true),
            null,
            null,
            bomBytes,
            bomBytes,
            [ExternalGuard("src/Feature.cs")]);
        Should.NotThrow(() => CreatePayload(effects: [bomEffect]));

        var wrongBom = CreateReplacementEffect(
            "src/Feature.cs",
            Encoding.UTF8.GetBytes("class After;\n"),
            sourceBefore: ExistingFile(bomBytes, hasByteOrderMark: false),
            originalContent: bomBytes);
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [wrongBom]))
            .Message.ShouldStartWith("UTF-8 BOM metadata does not match");

        var invalidUtf8 = new byte[] { 0xFF };
        var invalidUtf8Effect = CreateReplacementEffect(
            "src/Feature.cs",
            Encoding.UTF8.GetBytes("class After;\n"),
            sourceBefore: ExistingFile(invalidUtf8, newline: "none"),
            originalContent: invalidUtf8);
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [invalidUtf8Effect]))
            .Message.ShouldStartWith("Exact content is not valid UTF-8.");

        var wrongNewline = CreateReplacementEffect(
            "src/Feature.cs",
            Encoding.UTF8.GetBytes("class After;\n"),
            sourceBefore: ExistingFile("class Before;\n", newline: "none"));
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [wrongNewline]))
            .Message.ShouldStartWith("Newline metadata does not match");
    }

    [Fact]
    public void replace_and_move_preserve_reviewed_metadata_and_all_paths_obey_the_length_limit()
    {
        var originalLf = Encoding.UTF8.GetBytes("before\n");
        var replacementCrlf = Encoding.UTF8.GetBytes("after\r\n");
        var newlineChanged = NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null,
            ExistingFile(originalLf, newline: "lf"),
            ExistingFile(replacementCrlf, newline: "crlf"),
            null, null, originalLf, replacementCrlf, [ExternalGuard("src/Feature.cs")]);
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [newlineChanged]))
            .Message.ShouldStartWith("Replace and move effects must preserve exact file metadata");

        var executableBytes = Encoding.UTF8.GetBytes("script\n");
        var modeChanged = NewEffect(
            1, ChangePlanEffectKind.Replace, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null,
            ExistingFile(executableBytes),
            ExistingFile(executableBytes, fileMode: "0755", executable: true),
            null, null, executableBytes, executableBytes, [ExternalGuard("src/Feature.cs")]);
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [modeChanged]))
            .Message.ShouldStartWith("Replace and move effects must preserve exact file metadata");

        var move = NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs",
            ExistingFile(executableBytes), ChangePlanPathState.Missing,
            ChangePlanPathState.Missing,
            ExistingFile(executableBytes, platformFileIdentity: null, fileMode: "0755", executable: true),
            executableBytes, null,
            [ExternalGuard("src/Feature.cs"), ExternalGuard("src/Moved.cs")]);
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [move]))
            .Message.ShouldStartWith("Replace and move effects must preserve exact file metadata");

        var rewrittenBytes = Encoding.UTF8.GetBytes("rewritten\r\n");
        var rewrittenMove = NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs",
            ExistingFile(originalLf, newline: "lf"), ChangePlanPathState.Missing,
            ChangePlanPathState.Missing,
            ExistingFile(rewrittenBytes, platformFileIdentity: null, newline: "lf"),
            originalLf, rewrittenBytes,
            [ExternalGuard("src/Feature.cs"), ExternalGuard("src/Moved.cs")]);
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [rewrittenMove]))
            .Message.ShouldStartWith("Newline metadata does not match the exact content bytes.");

        var longPath = "src/" + new string('a', 250) + ".cs";
        var longEffect = CreateReplacementEffect(longPath, Encoding.UTF8.GetBytes("class After;\n"));
        Should.Throw<ArgumentException>(() => CreatePayload(effects: [longEffect]))
            .Message.ShouldContain("exceeds the conservative v1 path limit");

        var exactLimitPath = "src/" + new string(
            'c',
            259 - "/workspace/repository/".Length - "src/".Length);
        var exactLimitEffect = CreateReplacementEffect(
            exactLimitPath,
            Encoding.UTF8.GetBytes("class ExactLimit;\n"));
        Should.NotThrow(() => CreatePayload(effects: [exactLimitEffect]));

        var longDependencyPath = "inputs/" + new string('b', 248);
        var longDependency = new ChangePlanDependency(
            longDependencyPath,
            "/workspace/repository/" + longDependencyPath,
            ChangePlanDependencyKind.DirectoryEntry,
            DirectoryState("device:42;inode:300"),
            ExternalGuard(longDependencyPath));
        Should.Throw<ArgumentException>(() => CreatePayload(dependencies: [longDependency]))
            .Message.ShouldContain("exceeds the conservative v1 path limit");
    }

    [Fact]
    public void canonical_wire_names_cover_every_valid_enum_variant()
    {
        static JsonElement Root(ChangePlanPayload payload)
        {
            var envelope = ChangePlanEnvelope.Create(payload);
            var read = CanonicalChangePlanReader.ReadEnvelope(envelope.SerializeEnvelope()).Match(
                value => value,
                error => throw new InvalidOperationException(error.Message));
            read.SerializeEnvelope().ShouldBe(envelope.SerializeEnvelope());
            return JsonDocument.Parse(read.CanonicalPayloadBytes).RootElement.Clone();
        }

        var original = Encoding.UTF8.GetBytes("class Before;\n");
        var created = Encoding.UTF8.GetBytes("created\n");
        var create = NewEffect(
            1, ChangePlanEffectKind.Create, "src/New.cs", "/workspace/repository/src/New.cs",
            null, null, ChangePlanPathState.Missing, ExistingFile(created, platformFileIdentity: null),
            null, null, null, created, [ExternalGuard("src/New.cs")]);
        var delete = NewEffect(
            1, ChangePlanEffectKind.Delete, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            null, null, ExistingFile(original), ChangePlanPathState.Missing,
            null, null, original, null, [ExternalGuard("src/Feature.cs")]);
        var move = NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs",
            ExistingFile(original), ChangePlanPathState.Missing,
            ChangePlanPathState.Missing, ExistingFile(original, platformFileIdentity: null),
            original, null, [ExternalGuard("src/Moved.cs"), ExternalGuard("src/Feature.cs")]);

        var effects = new[]
        {
            (Effect: create, Token: "create"),
            (Effect: CreateReplacementEffect("src/Feature.cs", created), Token: "replace"),
            (Effect: delete, Token: "delete"),
            (Effect: move, Token: "move")
        };
        foreach (var (effect, token) in effects)
            Root(CreatePayload(effects: [effect])).GetProperty("effects")[0].GetProperty("kind").GetString().ShouldBe(token);

        foreach (var (kind, token) in new[]
                 {
                     (ChangePlanArtifactKind.File, "file"),
                     (ChangePlanArtifactKind.ProjectFile, "projectFile"),
                     (ChangePlanArtifactKind.RoslynDocument, "roslynDocument"),
                     (ChangePlanArtifactKind.AdditionalDocument, "additionalDocument"),
                     (ChangePlanArtifactKind.AnalyzerConfigDocument, "analyzerConfigDocument")
                 })
        {
            var effect = CreateReplacementEffect("src/Feature.cs", created, artifactKind: kind);
            Root(CreatePayload(effects: [effect])).GetProperty("effects")[0]
                .GetProperty("artifactKind").GetString().ShouldBe(token);
        }

        foreach (var (kind, token) in new[]
                 {
                     (ChangePlanDependencyKind.SemanticInput, "semanticInput"),
                     (ChangePlanDependencyKind.ProjectInput, "projectInput")
                 })
        {
            var dependency = new ChangePlanDependency(
                "inputs/input.txt",
                "/workspace/repository/inputs/input.txt",
                kind,
                ExistingFile("input\n", "device:42;inode:400"),
                ExternalGuard("inputs/input.txt"));
            Root(CreatePayload(dependencies: [dependency])).GetProperty("dependencies")[0]
                .GetProperty("kind").GetString().ShouldBe(token);
        }

        foreach (var (severity, token) in new[]
                 {
                     (MutationDiagnosticSeverity.Information, "information"),
                     (MutationDiagnosticSeverity.Warning, "warning"),
                     (MutationDiagnosticSeverity.Error, "error")
                 })
        {
            var diagnostic = new ChangePlanDiagnostic("code", severity, "message", []);
            Root(CreatePayload(diagnostics: [diagnostic])).GetProperty("diagnostics")[0]
                .GetProperty("severity").GetString().ShouldBe(token);
        }

        foreach (var (state, token) in new[]
                 {
                     (WorkspaceCompletenessState.Complete, "complete"),
                     (WorkspaceCompletenessState.Partial, "partial"),
                     (WorkspaceCompletenessState.Failed, "failed")
                 })
        {
            var workspace = CreateWorkspace(state);
            Root(CreatePayload(workspace: workspace)).GetProperty("workspace")
                .GetProperty("state").GetString().ShouldBe(token);
        }

        Root(CreatePayload(coordination: CreateCoordination(domain: ChangePlanCoordinationDomain.SharedAuthority)))
            .GetProperty("coordination").GetProperty("domain").GetString().ShouldBe("sharedAuthority");

        var conditionalGuard = new ChangePlanGuardRequirement(
            "src/Feature.cs", ChangePlanGuardKind.ConditionalEffect, "conditional-v1");
        var conditionalEffect = CreateReplacementEffect(
            "src/Feature.cs", created, guards: [conditionalGuard]);
        var conditionalDependency = CreateDirectoryDependency(
            "src", ChangePlanGuardKind.ConditionalEffect);
        var conditional = CreatePayload(
            effects: [conditionalEffect],
            dependencies: [conditionalDependency],
            coordination: CreateCoordination(mode: ChangePlanCoordinationMode.ConditionalEffects));
        Root(conditional).GetProperty("coordination").GetProperty("mode").GetString()
            .ShouldBe("conditionalEffects");
        Root(conditional).GetProperty("effects")[0].GetProperty("guards")[0]
            .GetProperty("kind").GetString().ShouldBe("conditionalEffect");

        Root(CreatePayload(pathPolicy: RejectPathPolicy(ChangePlanCaseSensitivity.Insensitive)))
            .GetProperty("pathPolicy").GetProperty("caseSensitivity").GetString().ShouldBe("insensitive");
    }

    [Fact]
    public void absent_content_and_move_destination_states_are_explicit_in_canonical_json()
    {
        var created = Encoding.UTF8.GetBytes("created\n");
        var create = NewEffect(
            1, ChangePlanEffectKind.Create, "src/New.cs", "/workspace/repository/src/New.cs",
            null, null, ChangePlanPathState.Missing, ExistingFile(created, platformFileIdentity: null),
            null, null, null, created, [ExternalGuard("src/New.cs")]);
        using var createDocument = JsonDocument.Parse(CanonicalChangePlan.Serialize(CreatePayload(effects: [create])));
        var createJson = createDocument.RootElement.GetProperty("effects")[0];
        createJson.GetProperty("originalContentBase64").ValueKind.ShouldBe(JsonValueKind.Null);
        createJson.GetProperty("sourceBefore").GetProperty("linkCount").ValueKind.ShouldBe(JsonValueKind.Null);

        var original = Encoding.UTF8.GetBytes("before\n");
        var move = NewEffect(
            1, ChangePlanEffectKind.Move, "src/Feature.cs", "/workspace/repository/src/Feature.cs",
            "src/Moved.cs", "/workspace/repository/src/Moved.cs",
            ExistingFile(original), ChangePlanPathState.Missing,
            ChangePlanPathState.Missing, ExistingFile(original, platformFileIdentity: null),
            original, null, [ExternalGuard("src/Feature.cs"), ExternalGuard("src/Moved.cs")]);
        using var moveDocument = JsonDocument.Parse(CanonicalChangePlan.Serialize(CreatePayload(effects: [move])));
        var moveJson = moveDocument.RootElement.GetProperty("effects")[0];
        moveJson.GetProperty("replacementContentBase64").ValueKind.ShouldBe(JsonValueKind.Null);
        moveJson.GetProperty("destinationAfter").GetProperty("exists").GetBoolean().ShouldBeTrue();

        using var envelopeDocument = JsonDocument.Parse(ChangePlanEnvelope.Create(CreatePayload()).SerializeEnvelope());
        envelopeDocument.RootElement.EnumerateObject().Count().ShouldBe(2);
        envelopeDocument.RootElement.GetProperty("payload").GetProperty("schemaVersion").GetString()
            .ShouldBe("rt.change-plan.v1");
    }

    [Fact]
    public void canonical_envelope_reader_rehydrates_the_exact_persisted_bytes()
    {
        var original = ChangePlanEnvelope.Create(CreatePayload());
        var bytes = original.SerializeEnvelope();

        var read = CanonicalChangePlanReader.ReadEnvelope(bytes).Match(
            value => value,
            error => throw new InvalidOperationException(error.Message));

        read.PlanHash.ShouldBe(original.PlanHash);
        read.Payload.PlanId.ShouldBe(original.Payload.PlanId);
        read.Payload.Repository.RepositoryId.ShouldBe(original.Payload.Repository.RepositoryId);
        read.SerializeEnvelope().ShouldBe(bytes);
    }

    [Fact]
    public void canonical_envelope_reader_rejects_noncanonical_or_incompatible_bytes()
    {
        var envelope = ChangePlanEnvelope.Create(CreatePayload());
        var canonical = Encoding.UTF8.GetString(envelope.SerializeEnvelope());
        var noncanonical = Encoding.UTF8.GetBytes(canonical.Replace(
            "{\"planHash\"",
            "{ \"planHash\"",
            StringComparison.Ordinal));
        var incompatible = Encoding.UTF8.GetBytes(canonical.Replace(
            ChangePlanSchema.Version,
            "rt.change-plan.v999",
            StringComparison.Ordinal));

        ReadError(noncanonical).Kind.ShouldBe(ChangePlanEnvelopeReadErrorKind.IntegrityFailure);
        ReadError(incompatible).Kind.ShouldBe(ChangePlanEnvelopeReadErrorKind.SchemaIncompatible);
    }

    [Fact]
    public void canonical_envelope_reader_rejects_null_and_unknown_fixed_policy_tokens_exactly()
    {
        Should.Throw<ArgumentNullException>(() => CanonicalChangePlanReader.ReadEnvelope(null!))
            .ParamName.ShouldBe("envelopeBytes");

        var envelope = ChangePlanEnvelope.Create(CreatePayload());
        var unknownPolicy = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(envelope.SerializeEnvelope()).Replace(
                "\"longPath\":\"reject\"",
                "\"longPath\":\"allow\"",
                StringComparison.Ordinal));

        var error = ReadError(unknownPolicy);
        error.Kind.ShouldBe(ChangePlanEnvelopeReadErrorKind.Malformed);
        error.Message.ShouldContain("allow");
        error.Message.ShouldContain("reject");
    }

    [Fact]
    public void canonical_envelope_reader_types_malformed_json_fields_and_model_values()
    {
        var envelopeBytes = ChangePlanEnvelope.Create(CreatePayload()).SerializeEnvelope();
        var corruptions = new (Action<JsonObject> Corrupt, string Evidence)[]
        {
            (root => root["planHash"] = 1, "planHash"),
            (root => root["payload"]!["repository"]!["id"] = "", "id"),
            (root => root["payload"]!["operations"] = new JsonObject(), "operations"),
            (root => root["payload"]!["assumptions"] = new JsonArray(1), "assumptions"),
            (root => root["payload"]!["effects"]![0]!["guards"]![0]!["required"] = false, "guard")
        };

        ReadError(Encoding.UTF8.GetBytes("{")).Kind
            .ShouldBe(ChangePlanEnvelopeReadErrorKind.Malformed);
        var rootError = ReadError(Encoding.UTF8.GetBytes("[]"));
        rootError.Kind.ShouldBe(ChangePlanEnvelopeReadErrorKind.Malformed);
        rootError.Message.ShouldContain("envelope");
        foreach (var (corrupt, evidence) in corruptions)
        {
            var root = JsonNode.Parse(envelopeBytes)!.AsObject();
            corrupt(root);
            var error = ReadError(Encoding.UTF8.GetBytes(root.ToJsonString()));
            error.Kind.ShouldBe(ChangePlanEnvelopeReadErrorKind.Malformed);
            error.Message.ShouldContain(evidence);
        }
    }

    [Fact]
    public void canonical_envelope_reader_reports_missing_and_wrong_scalar_properties()
    {
        var envelopeBytes = ChangePlanEnvelope.Create(CreatePayload()).SerializeEnvelope();
        var corruptions = new (Action<JsonObject> Corrupt, string Property)[]
        {
            (root => root.Remove("planHash"), "planHash"),
            (root => root["payload"]!["effects"]![0]!["destinationPath"] = 1, "destinationPath"),
            (root => root["payload"]!["effects"]![0]!["originalContentBase64"] = true, "originalContentBase64"),
            (root => root["payload"]!["operations"]![0]!["sequence"] = "one", "sequence"),
            (root => root["payload"]!["effects"]![0]!["sourceBefore"]!["linkCount"] = "one", "linkCount"),
            (root => root["payload"]!["verificationActions"]![0]!["required"] = "yes", "required"),
            (root => root["payload"]!["effects"]![0]!["sourceBefore"]!["readOnly"] = "yes", "readOnly")
        };

        foreach (var (corrupt, property) in corruptions)
        {
            var root = JsonNode.Parse(envelopeBytes)!.AsObject();
            corrupt(root);
            var error = ReadError(Encoding.UTF8.GetBytes(root.ToJsonString()));
            error.Kind.ShouldBe(ChangePlanEnvelopeReadErrorKind.Malformed);
            error.Message.ShouldContain(property);
        }
    }

    [Fact]
    public void canonical_envelope_reader_reports_the_malformed_structural_role()
    {
        var envelopeBytes = ChangePlanEnvelope.Create(CreatePayload()).SerializeEnvelope();
        var corruptions = new (Action<JsonObject> Corrupt, string Role)[]
        {
            (root => root["payload"]!["repository"] = 1, "repository"),
            (root => root["payload"]!["coordination"] = 1, "coordination"),
            (root => root["payload"]!["pathPolicy"] = 1, "pathPolicy"),
            (root => root["payload"]!["operations"]![0] = 1, "operation"),
            (root => root["payload"]!["dependencies"]![0] = 1, "dependency"),
            (root => root["payload"]!["effects"]![0] = 1, "effect"),
            (root => root["payload"]!["effects"]![0]!["sourceBefore"] = 1, "path state"),
            (root => root["payload"]!["effects"]![0]!["guards"]![0] = 1, "guard"),
            (root => root["payload"]!["diagnostics"]![0] = 1, "diagnostic"),
            (root => root["payload"]!["operations"]![0]!["arguments"]![0] = 1, "name/value pair"),
            (root => root["payload"]!["confidence"] = 1, "confidence"),
            (root => root["payload"]!["workspace"] = 1, "workspace"),
            (root => root["payload"]!["verificationActions"]![0] = 1, "verification action")
        };

        foreach (var (corrupt, role) in corruptions)
        {
            var root = JsonNode.Parse(envelopeBytes)!.AsObject();
            corrupt(root);
            var error = ReadError(Encoding.UTF8.GetBytes(root.ToJsonString()));
            error.Kind.ShouldBe(ChangePlanEnvelopeReadErrorKind.Malformed);
            error.Message.ShouldContain(role);
        }
    }

    [Fact]
    public void canonical_envelope_reader_rejects_every_unknown_wire_enum_token()
    {
        var envelopeBytes = ChangePlanEnvelope.Create(CreatePayload()).SerializeEnvelope();
        var corruptions = new Action<JsonObject>[]
        {
            root => root["payload"]!["effects"]![0]!["kind"] = "unknown",
            root => root["payload"]!["effects"]![0]!["artifactKind"] = "unknown",
            root => root["payload"]!["effects"]![0]!["sourceBefore"]!["kind"] = "unknown",
            root => root["payload"]!["effects"]![0]!["guards"]![0]!["kind"] = "unknown",
            root => root["payload"]!["dependencies"]![0]!["kind"] = "unknown",
            root => root["payload"]!["diagnostics"]![0]!["severity"] = "unknown",
            root => root["payload"]!["workspace"]!["state"] = "unknown",
            root => root["payload"]!["coordination"]!["domain"] = "unknown",
            root => root["payload"]!["coordination"]!["mode"] = "unknown",
            root => root["payload"]!["pathPolicy"]!["caseSensitivity"] = "unknown"
        };

        foreach (var corrupt in corruptions)
        {
            var root = JsonNode.Parse(envelopeBytes)!.AsObject();
            corrupt(root);
            var error = ReadError(Encoding.UTF8.GetBytes(root.ToJsonString()));
            error.Kind.ShouldBe(ChangePlanEnvelopeReadErrorKind.Malformed);
            error.Message.ShouldContain("Unsupported");
        }
    }

    [Fact]
    public void canonical_envelope_reader_round_trips_every_effect_shape()
    {
        var before = Encoding.UTF8.GetBytes("before\n");
        var after = Encoding.UTF8.GetBytes("after\n");
        var effects = new[]
        {
            NewEffect(
                1, ChangePlanEffectKind.Create, "src/New.cs", "/workspace/repository/src/New.cs",
                null, null, ChangePlanPathState.Missing,
                ExistingFile(after, platformFileIdentity: null), null, null,
                null, after, [ExternalGuard("src/New.cs")]),
            CreateReplacementEffect("src/Feature.cs", after),
            NewEffect(
                1, ChangePlanEffectKind.Delete, "src/Old.cs", "/workspace/repository/src/Old.cs",
                null, null, ExistingFile(before), ChangePlanPathState.Missing, null, null,
                before, null, [ExternalGuard("src/Old.cs")]),
            NewEffect(
                1, ChangePlanEffectKind.Move, "src/Old.cs", "/workspace/repository/src/Old.cs",
                "src/New.cs", "/workspace/repository/src/New.cs",
                ExistingFile(before), ChangePlanPathState.Missing,
                ChangePlanPathState.Missing, ExistingFile(before, platformFileIdentity: null),
                before, null, [ExternalGuard("src/Old.cs"), ExternalGuard("src/New.cs")])
        };

        foreach (var effect in effects)
        {
            var envelope = ChangePlanEnvelope.Create(CreatePayload(effects: [effect]));
            var read = CanonicalChangePlanReader.ReadEnvelope(envelope.SerializeEnvelope()).Match(
                value => value,
                error => throw new InvalidOperationException(error.Message));

            read.SerializeEnvelope().ShouldBe(envelope.SerializeEnvelope());
        }
    }

    [Fact]
    public void exact_diff_is_derived_for_create_replace_delete_and_move()
    {
        var beforeBytes = Encoding.UTF8.GetBytes("before\n");
        var afterBytes = Encoding.UTF8.GetBytes("after\n");
        var create = NewEffect(
            1, ChangePlanEffectKind.Create, "src/New.cs", "/workspace/repository/src/New.cs",
            null, null, ChangePlanPathState.Missing, ExistingFile(afterBytes, platformFileIdentity: null),
            null, null, null, afterBytes, [ExternalGuard("src/New.cs")], ChangePlanArtifactKind.File);
        var delete = NewEffect(
            1, ChangePlanEffectKind.Delete, "src/Old.cs", "/workspace/repository/src/Old.cs",
            null, null, ExistingFile(beforeBytes), ChangePlanPathState.Missing,
            null, null, beforeBytes, null, [ExternalGuard("src/Old.cs")], ChangePlanArtifactKind.ProjectFile);
        var move = NewEffect(
            1, ChangePlanEffectKind.Move, "src/Old.cs", "/workspace/repository/src/Old.cs",
            "src/New.cs", "/workspace/repository/src/New.cs",
            ExistingFile(beforeBytes), ChangePlanPathState.Missing,
            ChangePlanPathState.Missing, ExistingFile(beforeBytes, platformFileIdentity: null),
            beforeBytes, null, [ExternalGuard("src/Old.cs"), ExternalGuard("src/New.cs")],
            ChangePlanArtifactKind.AdditionalDocument);

        ChangePlanDiff.Render([create]).ShouldBe(
            $"effect 1 create file\nsource src/New.cs\ndestination null\nbeforeSha256 absent\n" +
            $"afterSha256 {Hash(afterBytes)}\nbeforeContentBase64 null\n" +
            $"afterContentBase64 {Convert.ToBase64String(afterBytes)}\n");
        ChangePlanDiff.Render([CreateReplacementEffect("src/Feature.cs", afterBytes)]).ShouldBe(
            $"effect 1 replace roslynDocument\nsource src/Feature.cs\ndestination null\n" +
            $"beforeSha256 {Hash(Encoding.UTF8.GetBytes("class Before;\n"))}\n" +
            $"afterSha256 {Hash(afterBytes)}\n" +
            $"beforeContentBase64 {Convert.ToBase64String(Encoding.UTF8.GetBytes("class Before;\n"))}\n" +
            $"afterContentBase64 {Convert.ToBase64String(afterBytes)}\n");
        ChangePlanDiff.Render([delete]).ShouldBe(
            $"effect 1 delete projectFile\nsource src/Old.cs\ndestination null\n" +
            $"beforeSha256 {Hash(beforeBytes)}\nafterSha256 absent\n" +
            $"beforeContentBase64 {Convert.ToBase64String(beforeBytes)}\nafterContentBase64 null\n");
        ChangePlanDiff.Render([move]).ShouldBe(
            $"effect 1 move additionalDocument\nsource src/Old.cs\ndestination src/New.cs\n" +
            $"beforeSha256 {Hash(beforeBytes)}\nafterSha256 {Hash(beforeBytes)}\n" +
            $"beforeContentBase64 {Convert.ToBase64String(beforeBytes)}\n" +
            $"afterContentBase64 {Convert.ToBase64String(beforeBytes)}\n");
        Should.Throw<ArgumentNullException>(() => ChangePlanDiff.Render(null!)).ParamName.ShouldBe("effects");
    }

    private static ChangePlanEnvelopeReadError ReadError(byte[] envelopeBytes) =>
        CanonicalChangePlanReader.ReadEnvelope(envelopeBytes).Match(
            _ => throw new InvalidOperationException("Expected the canonical envelope read to fail."),
            error =>
            {
                error.Message.ShouldNotBeNullOrWhiteSpace();
                return error;
            });

    private static ChangePlanPayload CreatePayload(
        string sourcePath = "src/Feature.cs",
        byte[]? replacementContent = null,
        IReadOnlyList<ChangePlanOperation>? operations = null,
        IReadOnlyList<ChangePlanValue>? operationArguments = null,
        IReadOnlyList<ChangePlanDependency>? dependencies = null,
        IReadOnlyList<ChangePlanEffect>? effects = null,
        IReadOnlyList<ChangePlanDiagnostic>? diagnostics = null,
        IReadOnlyList<string>? assumptions = null,
        IReadOnlyList<string>? limitations = null,
        ChangePlanRepository? repository = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        ChangePlanConfidence? confidence = null,
        ChangePlanWorkspace? workspace = null,
        IReadOnlyList<ChangePlanVerificationAction>? verificationActions = null,
        ChangePlanCoordination? coordination = null,
        ChangePlanPathPolicy? pathPolicy = null,
        ChangePlanCleanupPolicy cleanupPolicy = ChangePlanCleanupPolicy.RetainUntilVerifiedTerminal,
        ChangePlanRecoveryPolicy recoveryPolicy = ChangePlanRecoveryPolicy.RequireDurableJournal)
    {
        replacementContent ??= Encoding.UTF8.GetBytes("class After;\n");
        operationArguments ??=
        [
            new ChangePlanValue("symbol", "Example.Before"),
            new ChangePlanValue("newName", "After"),
            new ChangePlanValue("kind", null)
        ];
        operations ??= [new ChangePlanOperation(1, "rename", "Example.Before", operationArguments)];
        effects ??= [CreateReplacementEffect(sourcePath, replacementContent)];
        assumptions ??= ["The selected symbol is stable."];
        limitations ??= ["External consumers are unknown."];
        dependencies ??= [CreateDirectoryDependency("src")];
        diagnostics ??=
        [
            new ChangePlanDiagnostic(
                "rename.preview",
                MutationDiagnosticSeverity.Information,
                "One declaration and its references will be renamed.",
                [new ChangePlanValue("references", "3")])
        ];
        coordination ??= CreateCoordination();
        pathPolicy ??= new ChangePlanPathPolicy(
            ChangePlanCaseSensitivity.Sensitive,
            ChangePlanLongPathPolicy.Reject,
            ChangePlanReadOnlyPolicy.Reject,
            ChangePlanSymlinkPolicy.Reject,
            ChangePlanHardLinkPolicy.Reject);
        repository ??= new ChangePlanRepository(
            "repo-001",
            "/workspace/repository",
            "/workspace/repository",
            "device:42;inode:1",
            new string('a', 40),
            new string('b', 64));
        confidence ??= new ChangePlanConfidence(
            "Proof",
            "Complete semantic workspace at the recorded source fingerprint.",
            ["All requested projects loaded."]);
        workspace ??= new ChangePlanWorkspace(
            WorkspaceCompletenessState.Complete,
            "/workspace/repository/RoslynTools.slnx",
            "Debug",
            ["net10.0"],
            ".NET SDK 10.0.100",
            "Microsoft.CodeAnalysis 5.0.0",
            "InProcess",
            "Trusted");
        verificationActions ??=
        [
            new ChangePlanVerificationAction(
                1,
                "compile",
                "dotnet build RoslynTools.slnx --no-restore",
                "No compiler errors",
                required: true)
        ];

        return new ChangePlanPayload(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            repository,
            createdAtUtc ?? new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            expiresAtUtc ?? new DateTimeOffset(2030, 1, 2, 3, 9, 5, TimeSpan.Zero),
            operations,
            dependencies,
            effects,
            diagnostics,
            assumptions,
            confidence,
            limitations,
            workspace,
            verificationActions,
            coordination,
            pathPolicy,
            cleanupPolicy,
            recoveryPolicy);
    }

    private static ChangePlanEffect CreateReplacementEffect(
        string path,
        byte[] content,
        int sequence = 1,
        ChangePlanPathState? sourceBefore = null,
        string? sourceResolvedPath = null,
        ChangePlanPathState? sourceAfter = null,
        ChangePlanArtifactKind artifactKind = ChangePlanArtifactKind.RoslynDocument,
        byte[]? originalContent = null,
        IEnumerable<ChangePlanGuardRequirement>? guards = null) => new(
        sequence,
        ChangePlanEffectKind.Replace,
        artifactKind,
        path,
        sourceResolvedPath ?? $"/workspace/repository/{path.Replace('\\', '/').Replace("./", string.Empty, StringComparison.Ordinal)}",
        destinationPath: null,
        destinationResolvedPath: null,
        sourceBefore: sourceBefore ?? ExistingFile("class Before;\n"),
        sourceAfter: sourceAfter ?? ExistingFile(content),
        destinationBefore: null,
        destinationAfter: null,
        originalContent: originalContent ?? Encoding.UTF8.GetBytes("class Before;\n"),
        replacementContent: content,
        guards: guards ?? [ExternalGuard(path)]);

    private static ChangePlanRepository CreateRepository(
        string root = "/workspace/repository",
        string? resolvedRoot = null,
        string rootPlatformIdentity = "device:42;inode:1") => new(
        "repo-001",
        root,
        resolvedRoot ?? root,
        rootPlatformIdentity,
        new string('a', 40),
        new string('b', 64));

    private static ChangePlanEffect NewEffect(
        int sequence,
        ChangePlanEffectKind kind,
        string sourcePath,
        string sourceResolvedPath,
        string? destinationPath,
        string? destinationResolvedPath,
        ChangePlanPathState sourceBefore,
        ChangePlanPathState sourceAfter,
        ChangePlanPathState? destinationBefore,
        ChangePlanPathState? destinationAfter,
        byte[]? originalContent,
        byte[]? replacementContent,
        IEnumerable<ChangePlanGuardRequirement> guards,
        ChangePlanArtifactKind artifactKind = ChangePlanArtifactKind.File) => new(
        sequence,
        kind,
        artifactKind,
        sourcePath,
        sourceResolvedPath,
        destinationPath,
        destinationResolvedPath,
        sourceBefore,
        sourceAfter,
        destinationBefore,
        destinationAfter,
        originalContent,
        replacementContent,
        guards);

    private static ChangePlanDependency CreateDirectoryDependency(
        string path,
        ChangePlanGuardKind guardKind = ChangePlanGuardKind.ExternalWriteGuard,
        int linkCount = 5) => new(
        path,
        $"/workspace/repository/{path}",
        ChangePlanDependencyKind.DirectoryEntry,
        DirectoryState("device:42;inode:2", linkCount),
        new ChangePlanGuardRequirement(
            path,
            guardKind,
            expectedToken: "guard-directory-v1"));

    private static ChangePlanCoordination CreateCoordination(
        ChangePlanCoordinationDomain domain = ChangePlanCoordinationDomain.ExclusivePrincipal,
        ChangePlanCoordinationMode mode = ChangePlanCoordinationMode.ExternalWriteGuards,
        string? controlRoot = null,
        string? controlRootResolvedPath = null,
        string controlRootPlatformIdentity = "device:99;inode:1",
        string filesystemVolumeIdentity = "device:99",
        string? stagingRoot = null,
        string? stagingRootResolvedPath = null,
        string stagingRootPlatformIdentity = "device:99;inode:2",
        string? backupRoot = null,
        string? backupRootResolvedPath = null,
        string backupRootPlatformIdentity = "device:99;inode:3")
    {
        const string defaultControlRoot =
            "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555";
        const string defaultStagingRoot =
            "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555/staging";
        const string defaultBackupRoot =
            "/var/lib/roslyn-tools/operations/11111111-2222-3333-4444-555555555555/backup";
        controlRoot ??= defaultControlRoot;
        stagingRoot ??= defaultStagingRoot;
        backupRoot ??= defaultBackupRoot;
        return new ChangePlanCoordination(
            domain,
            mode,
            "single-principal plus complete affected-path guard set",
            controlRoot,
            controlRootResolvedPath ?? controlRoot,
            controlRootPlatformIdentity,
            filesystemVolumeIdentity,
            stagingRoot,
            stagingRootResolvedPath ?? stagingRoot,
            stagingRootPlatformIdentity,
            backupRoot,
            backupRootResolvedPath ?? backupRoot,
            backupRootPlatformIdentity);
    }

    private static ChangePlanWorkspace CreateWorkspace(WorkspaceCompletenessState state) => new(
        state,
        "/workspace/repository/RoslynTools.slnx",
        "Debug",
        ["net10.0"],
        ".NET SDK 10.0.100",
        "Microsoft.CodeAnalysis 5.0.0",
        "InProcess",
        "Trusted");

    private static ChangePlanGuardRequirement ExternalGuard(string path) => new(
        path,
        ChangePlanGuardKind.ExternalWriteGuard,
        expectedToken: "guard-v1");

    private static ChangePlanPathPolicy RejectPathPolicy(
        ChangePlanCaseSensitivity caseSensitivity = ChangePlanCaseSensitivity.Sensitive) => new(
        caseSensitivity,
        ChangePlanLongPathPolicy.Reject,
        ChangePlanReadOnlyPolicy.Reject,
        ChangePlanSymlinkPolicy.Reject,
        ChangePlanHardLinkPolicy.Reject);

    private static ChangePlanPathState DirectoryState(
        string platformFileIdentity,
        int linkCount = 5) => new(
        exists: true,
        ChangePlanPathKind.Directory,
        contentHash: null,
        platformFileIdentity,
        linkCount,
        knownHardLinkAliases: [],
        encodingName: null,
        hasByteOrderMark: null,
        newline: null,
        fileMode: "0755",
        executable: true,
        readOnly: false,
        symlinkTarget: null);

    private static ChangePlanPathState ExistingFile(
        string content,
        string? platformFileIdentity = "device:42;inode:84",
        int linkCount = 1,
        string? encodingName = "utf-8",
        bool? hasByteOrderMark = false,
        string? newline = "lf",
        string? fileMode = "0644",
        bool? executable = false,
        bool? readOnly = false,
        IEnumerable<string>? knownHardLinkAliases = null) =>
        ExistingFile(
            Encoding.UTF8.GetBytes(content),
            platformFileIdentity,
            linkCount,
            encodingName,
            hasByteOrderMark,
            newline,
            fileMode,
            executable,
            readOnly,
            knownHardLinkAliases);

    private static ChangePlanPathState ExistingFile(
        byte[] content,
        string? platformFileIdentity = "device:42;inode:84",
        int linkCount = 1,
        string? encodingName = "utf-8",
        bool? hasByteOrderMark = false,
        string? newline = "lf",
        string? fileMode = "0644",
        bool? executable = false,
        bool? readOnly = false,
        IEnumerable<string>? knownHardLinkAliases = null) => new(
        exists: true,
        ChangePlanPathKind.RegularFile,
        contentHash: Hash(content),
        platformFileIdentity,
        linkCount,
        knownHardLinkAliases: knownHardLinkAliases ?? [],
        encodingName,
        hasByteOrderMark,
        newline,
        fileMode,
        executable,
        readOnly,
        symlinkTarget: null);

    private static ChangePlanPathState RawState(
        bool exists,
        ChangePlanPathKind kind,
        string? contentHash = null,
        string? platformFileIdentity = null,
        int? linkCount = null,
        IEnumerable<string>? knownHardLinkAliases = null,
        string? encodingName = null,
        bool? hasByteOrderMark = null,
        string? newline = null,
        string? fileMode = null,
        bool? executable = null,
        bool? readOnly = null,
        string? symlinkTarget = null) => new(
        exists,
        kind,
        contentHash,
        platformFileIdentity,
        linkCount,
        knownHardLinkAliases ?? [],
        encodingName,
        hasByteOrderMark,
        newline,
        fileMode,
        executable,
        readOnly,
        symlinkTarget);

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
