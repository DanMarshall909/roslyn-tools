using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class DurableChangePlanStoreTests
{
    private static readonly DateTimeOffset InitialNow =
        new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void default_limits_are_stable_and_positive()
    {
        ChangePlanStoreOptions.Default.ShouldSatisfyAllConditions(
            options => options.MaximumPlanCount.ShouldBe(256),
            options => options.MaximumPlanBytes.ShouldBe(16L * 1024 * 1024),
            options => options.MaximumStoreBytes.ShouldBe(256L * 1024 * 1024),
            options => options.MaximumPinsPerPlan.ShouldBe(128),
            options => options.MaximumPinReferenceBytes.ShouldBe(256),
            options => options.MaximumDispatchLeaseLifetime.ShouldBe(TimeSpan.FromMinutes(15)),
            options => options.MaximumPlanLifetime.ShouldBe(TimeSpan.FromHours(24)),
            options => options.UnreferencedRetention.ShouldBe(TimeSpan.FromHours(24)));
    }

    [Fact]
    public void invalid_limit_boundaries_are_rejected()
    {
        var valid = ChangePlanStoreOptions.Default;
        var invalid = new[]
        {
            valid with { MaximumPlanCount = 0 },
            valid with { MaximumPlanCount = -1 },
            valid with { MaximumPlanBytes = 0 },
            valid with { MaximumPlanBytes = -1 },
            valid with { MaximumStoreBytes = valid.MaximumPlanBytes - 1 },
            valid with { MaximumPinsPerPlan = 0 },
            valid with { MaximumPinsPerPlan = -1 },
            valid with { MaximumPinReferenceBytes = 0 },
            valid with { MaximumPinReferenceBytes = -1 },
            valid with { MaximumDispatchLeaseLifetime = TimeSpan.Zero },
            valid with { MaximumDispatchLeaseLifetime = -TimeSpan.FromTicks(1) },
            valid with { MaximumPlanLifetime = TimeSpan.Zero },
            valid with { MaximumPlanLifetime = -TimeSpan.FromTicks(1) },
            valid with { UnreferencedRetention = -TimeSpan.FromTicks(1) }
        };

        foreach (var options in invalid)
            Should.Throw<ArgumentOutOfRangeException>(options.Validate);

        (valid with { MaximumStoreBytes = valid.MaximumPlanBytes }).Validate();
        (valid with { UnreferencedRetention = TimeSpan.Zero }).Validate();
    }

    [Fact]
    public void constructor_and_persist_validate_required_arguments()
    {
        Should.Throw<ArgumentException>(() => new DurableChangePlanStore(""))
            .ParamName.ShouldBe("rootPath");
        Should.Throw<ArgumentNullException>(() => new DurableChangePlanStore(null!))
            .ParamName.ShouldBe("rootPath");
        Should.Throw<ArgumentNullException>(() => new DurableChangePlanStore("/tmp/plan-store").Persist(null!))
            .ParamName.ShouldBe("envelope");
        var optionsError = Should.Throw<ArgumentOutOfRangeException>(() => new DurableChangePlanStore(
            "/tmp/plan-store",
            ChangePlanStoreOptions.Default with
            {
                MaximumStoreBytes = ChangePlanStoreOptions.Default.MaximumPlanBytes - 1
            }));
        optionsError.Message.ShouldContain("Store capacity");
    }

    [Fact]
    public void a_new_process_view_loads_the_exact_persisted_envelope()
    {
        using var fixture = new StoreFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.CreateStore().Persist(envelope));

        var loaded = Unwrap(fixture.CreateStore().Load(key));

        loaded.EnvelopeBytes.AsSpan().SequenceEqual(envelope.SerializeEnvelope()).ShouldBeTrue();
        loaded.Envelope.SerializeEnvelope().ShouldBe(envelope.SerializeEnvelope());
        loaded.Key.ShouldBe(key);
    }

    [Fact]
    public void persistence_is_idempotent_for_the_same_canonical_key_and_bytes()
    {
        using var fixture = new StoreFixture();
        var envelope = fixture.CreateEnvelope();
        var store = fixture.CreateStore();

        var first = Unwrap(store.Persist(envelope));
        Unwrap(store.PinDispatchLease(
            first,
            "dispatch-preserved",
            fixture.Now + TimeSpan.FromMinutes(10)));
        var before = Unwrap(store.Load(first));
        var recordBytes = File.ReadAllBytes(fixture.RecordPath);
        fixture.Now += TimeSpan.FromMinutes(5);
        var second = Unwrap(store.Persist(envelope));
        var after = Unwrap(store.Load(first));

        second.ShouldBe(first);
        after.PersistedAtUtc.ShouldBe(before.PersistedAtUtc);
        after.RetainUntilUtc.ShouldBe(before.RetainUntilUtc);
        after.Pins.ShouldBe(before.Pins);
        File.ReadAllBytes(fixture.RecordPath).ShouldBe(recordBytes);
        Directory.EnumerateFiles(fixture.StoreRoot, "*.plan", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void an_unproven_physical_store_boundary_fails_closed()
    {
        using var fixture = new StoreFixture();
        var unprovenStore = new DurableChangePlanStore(
            fixture.StoreRoot,
            fixture.Options,
            () => fixture.Now);

        Error(unprovenStore.Persist(fixture.CreateEnvelope())).Kind
            .ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
        Directory.Exists(fixture.StoreRoot).ShouldBeFalse();

        var key = Unwrap(fixture.CreateStore().Persist(fixture.CreateEnvelope()));
        Error(unprovenStore.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
    }

    [Fact]
    public void persistence_holds_the_physical_protection_lease_through_durable_publication()
    {
        using var fixture = new StoreFixture();
        var lease = new TrackingProtectionLease();
        var authority = new TrackingProtectionAuthority(fixture.StoreRoot, lease);
        var durability = new LeaseAssertingDurability(lease);

        Unwrap(fixture.CreateStore(
            protectionAuthority: authority,
            durability: durability).Persist(fixture.CreateEnvelope()));

        durability.Events.ShouldNotBeEmpty();
        lease.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void persistence_disposes_a_lease_returned_with_a_protection_error()
    {
        using var fixture = new StoreFixture();
        var lease = new TrackingProtectionLease();
        var expectedError = new ChangePlanStoreError(
            ChangePlanStoreErrorKind.UnsafeProtection,
            "Injected physical protection rejection.");
        var authority = new ErrorAndLeaseProtectionAuthority(expectedError, lease);

        var error = Error(fixture.CreateStore(
            protectionAuthority: authority).Persist(fixture.CreateEnvelope()));

        error.ShouldBe(expectedError);
        lease.DisposeCount.ShouldBe(1);
        Directory.Exists(fixture.StoreRoot).ShouldBeFalse();
    }

    [Fact]
    public void persistence_disposes_an_assigned_lease_when_the_protection_authority_throws()
    {
        using var fixture = new StoreFixture();
        var lease = new TrackingProtectionLease();
        var authority = new LeaseThenThrowingProtectionAuthority(lease);

        var error = Error(fixture.CreateStore(
            protectionAuthority: authority).Persist(fixture.CreateEnvelope()));

        error.Kind.ShouldBe(ChangePlanStoreErrorKind.Unreadable);
        lease.DisposeCount.ShouldBe(1);
        Directory.Exists(fixture.StoreRoot).ShouldBeFalse();
    }

    [Fact]
    public void persistence_rejects_a_successful_protection_result_without_a_lease()
    {
        using var fixture = new StoreFixture();

        var error = Error(fixture.CreateStore(
            protectionAuthority: new MissingLeaseProtectionAuthority()).Persist(fixture.CreateEnvelope()));

        error.Kind.ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
        Directory.Exists(fixture.StoreRoot).ShouldBeFalse();
    }

    [Fact]
    public void persistence_rechecks_expiry_at_the_locked_transition()
    {
        using var fixture = new StoreFixture();
        var envelope = fixture.CreateEnvelope(expiresAtUtc: fixture.Now + TimeSpan.FromMinutes(1));
        var clockReads = 0;
        var store = fixture.CreateStore(utcNow: () => Interlocked.Increment(ref clockReads) == 1
            ? fixture.Now
            : envelope.Payload.ExpiresAtUtc);

        Error(store.Persist(envelope)).Kind.ShouldBe(ChangePlanStoreErrorKind.Expired);
        Directory.EnumerateFiles(fixture.StoreRoot, "*.plan", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void persistence_rechecks_future_creation_at_the_locked_transition()
    {
        using var fixture = new StoreFixture();
        var envelope = fixture.CreateEnvelope(createdAtUtc: fixture.Now);
        var clockReads = 0;
        var store = fixture.CreateStore(utcNow: () => Interlocked.Increment(ref clockReads) == 1
            ? fixture.Now
            : fixture.Now - TimeSpan.FromTicks(1));

        var error = Error(store.Persist(envelope));

        error.Kind.ShouldBe(ChangePlanStoreErrorKind.Conflict);
        error.Message.ShouldContain("persistence boundary");
        Directory.EnumerateFiles(fixture.StoreRoot, "*.plan", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void persistence_cleans_interrupted_store_artifacts_before_publication()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        Unwrap(store.Persist(fixture.CreateEnvelope()));
        var directory = Path.GetDirectoryName(fixture.RecordPath)!;
        var tombstone = Path.Combine(directory, "interrupted.plan.gc-before-persist");
        var temporary = Path.Combine(directory, ".interrupted.plan.tmp");
        File.WriteAllText(tombstone, "obsolete");
        File.WriteAllText(temporary, "incomplete");

        Unwrap(store.Persist(fixture.CreateEnvelope(
            planId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"))));

        File.Exists(tombstone).ShouldBeFalse();
        File.Exists(temporary).ShouldBeFalse();
    }

    [Fact]
    public void failed_record_publication_removes_its_complete_temporary_file()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        Unwrap(store.Persist(fixture.CreateEnvelope()));
        var secondEnvelope = fixture.CreateEnvelope(
            planId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var secondKey = new ChangePlanStoreKey(
            secondEnvelope.Payload.Repository.RepositoryId,
            secondEnvelope.Payload.PlanId,
            secondEnvelope.PlanHash);
        var obstructedRecordPath = fixture.ExpectedRecordPath(secondKey);
        Directory.CreateDirectory(obstructedRecordPath);

        var error = Error(store.Persist(secondEnvelope));
        error.Kind.ShouldBe(ChangePlanStoreErrorKind.Unreadable);
        error.Evidence.ShouldNotBeNull()["store"].ShouldBe(fixture.StoreRoot);

        Directory.EnumerateFiles(
                Path.GetDirectoryName(obstructedRecordPath)!,
                "*.tmp",
                SearchOption.TopDirectoryOnly)
            .ShouldBeEmpty();
    }

    [Fact]
    public void publication_flushes_file_metadata_before_replace_and_directory_after_replace()
    {
        using var fixture = new StoreFixture();
        var durability = new RecordingDurability();

        Unwrap(fixture.CreateStore(durability: durability).Persist(fixture.CreateEnvelope()));

        var replaceIndex = durability.Events.FindIndex(value => value.StartsWith("replace:", StringComparison.Ordinal));
        replaceIndex.ShouldBeGreaterThan(0);
        durability.Events[replaceIndex - 1].ShouldStartWith("flush-file:");
        durability.Events[replaceIndex + 1].ShouldBe(
            $"flush-directory:{Path.GetDirectoryName(fixture.RecordPath)}");
        var recordDirectory = Path.GetDirectoryName(fixture.RecordPath)!;
        var recordParent = Path.GetDirectoryName(recordDirectory)!;
        durability.Events.Count(value => value == $"flush-directory:{recordDirectory}")
            .ShouldBeGreaterThanOrEqualTo(2);
        durability.Events.Count(value => value == $"flush-directory:{recordParent}")
            .ShouldBeGreaterThanOrEqualTo(2);
        durability.Events.Count(value => value == $"flush-directory:{fixture.StoreRoot}")
            .ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void a_post_replace_directory_flush_failure_returns_uncertainty_and_exact_retry_is_safe()
    {
        using var fixture = new StoreFixture();
        var envelope = fixture.CreateEnvelope();
        var durability = new FaultAfterReplaceDurability();

        Error(fixture.CreateStore(durability: durability).Persist(envelope)).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Unreadable);
        File.Exists(fixture.RecordPath).ShouldBeTrue();

        var retried = Unwrap(fixture.CreateStore().Persist(envelope));
        Unwrap(fixture.CreateStore().Load(retried)).Envelope.SerializeEnvelope()
            .ShouldBe(envelope.SerializeEnvelope());
    }

    [Fact]
    public async Task concurrent_process_views_publish_one_complete_record()
    {
        using var fixture = new StoreFixture();
        var envelope = fixture.CreateEnvelope();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            fixture.CreateStore().Persist(envelope))));

        results.ShouldAllBe(result => result.IsOk);
        Directory.EnumerateFiles(fixture.StoreRoot, "*.plan", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();
        Unwrap(fixture.CreateStore().Load(Unwrap(results[0]))).EnvelopeBytes
            .AsSpan().SequenceEqual(envelope.SerializeEnvelope()).ShouldBeTrue();
    }

    [Fact]
    public void persisted_directories_and_records_are_private_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new StoreFixture();
        Unwrap(fixture.CreateStore().Persist(fixture.CreateEnvelope()));

        File.GetUnixFileMode(fixture.StoreRoot).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.GetUnixFileMode(Path.Combine(fixture.StoreRoot, ".store.lock")).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        foreach (var directory in Directory.EnumerateDirectories(
                     fixture.StoreRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.GetUnixFileMode(directory).ShouldBe(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        File.GetUnixFileMode(fixture.RecordPath).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void weakened_record_permissions_fail_closed_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        File.SetUnixFileMode(
            fixture.RecordPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        Error(store.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
    }

    [Fact]
    public void weakened_store_root_permissions_fail_closed_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        File.SetUnixFileMode(
            fixture.StoreRoot,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead);

        var untrustedLoad = fixture.CreateStore(protectionAuthority: new ThrowingProtectionAuthority());
        Error(untrustedLoad.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
    }

    [Fact]
    public void persistence_repairs_a_preexisting_store_root_to_owner_only_permissions_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new StoreFixture();
        Directory.CreateDirectory(fixture.StoreRoot);
        File.SetUnixFileMode(
            fixture.StoreRoot,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute);

        Unwrap(fixture.CreateStore().Persist(fixture.CreateEnvelope()));

        File.GetUnixFileMode(fixture.StoreRoot).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void persistence_fails_closed_for_a_preexisting_nonprivate_plan_directory_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new StoreFixture();
        var plansPath = Path.Combine(fixture.StoreRoot, "plans");
        Directory.CreateDirectory(plansPath);
        File.SetUnixFileMode(
            fixture.StoreRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(
            plansPath,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute);

        var error = Error(fixture.CreateStore().Persist(fixture.CreateEnvelope()));

        error.Kind.ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
        Directory.EnumerateFiles(plansPath, "*.plan", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void storage_failures_from_the_physical_protection_authority_are_typed()
    {
        using (var persistenceFixture = new StoreFixture())
        {
            var persistenceStore = persistenceFixture.CreateStore(
                protectionAuthority: new ThrowingProtectionAuthority());

            var persistenceError = Error(persistenceStore.Persist(persistenceFixture.CreateEnvelope()));

            persistenceError.Kind.ShouldBe(ChangePlanStoreErrorKind.Unreadable);
            persistenceError.Evidence.ShouldNotBeNull()["store"].ShouldBe(persistenceFixture.StoreRoot);
            Directory.Exists(persistenceFixture.StoreRoot).ShouldBeFalse();
        }

        using var fixture = new StoreFixture();
        var key = Unwrap(fixture.CreateStore().Persist(fixture.CreateEnvelope()));
        var store = fixture.CreateStore(protectionAuthority: new ThrowingProtectionAuthority());

        var error = Error(store.Load(key));

        error.Kind.ShouldBe(ChangePlanStoreErrorKind.Unreadable);
        error.Evidence.ShouldNotBeNull()["store"].ShouldBe(fixture.StoreRoot);
    }

    [Fact]
    public void a_symlinked_store_root_fails_closed_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new StoreFixture();
        var target = Path.Combine(Path.GetDirectoryName(fixture.StoreRoot)!, "store-target");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(fixture.StoreRoot, target);

        Error(fixture.CreateStore().Persist(fixture.CreateEnvelope())).Kind
            .ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
    }

    [Fact]
    public void repository_controlled_or_wrong_authority_store_roots_are_rejected()
    {
        using var fixture = new StoreFixture();
        var envelope = fixture.CreateEnvelope();
        var repositoryStore = new DurableChangePlanStore(
            Path.Combine(fixture.RepositoryRoot, ".rt-plans"),
            fixture.Options,
            () => fixture.Now);
        var sharedEnvelope = fixture.CreateEnvelope(
            planId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            domain: ChangePlanCoordinationDomain.SharedAuthority);

        Error(repositoryStore.Persist(envelope)).Kind.ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
        Error(fixture.CreateStore().Persist(sharedEnvelope)).Kind.ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
        Directory.Exists(Path.Combine(fixture.RepositoryRoot, ".rt-plans")).ShouldBeFalse();
    }

    [Fact]
    public void repository_root_cannot_contain_the_protected_store()
    {
        using var repositoryInsideStore = new StoreFixture();
        var nestedRepository = Path.Combine(repositoryInsideStore.StoreRoot, "repository");
        var nestedEnvelope = repositoryInsideStore.CreateEnvelope(
            repositoryRoot: nestedRepository,
            resolvedRepositoryRoot: nestedRepository);

        Error(repositoryInsideStore.CreateStore().Persist(nestedEnvelope)).Kind
            .ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);

        var identicalStore = new DurableChangePlanStore(
            repositoryInsideStore.RepositoryRoot,
            repositoryInsideStore.Options,
            () => repositoryInsideStore.Now,
            new TestProtectionAuthority(repositoryInsideStore.RepositoryRoot));
        Error(identicalStore.Persist(repositoryInsideStore.CreateEnvelope())).Kind
            .ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);

        using var similarlyNamedSibling = new StoreFixture();
        var siblingRoot = $"{similarlyNamedSibling.StoreRoot}-sibling";
        var siblingEnvelope = similarlyNamedSibling.CreateEnvelope(
            repositoryRoot: siblingRoot,
            resolvedRepositoryRoot: siblingRoot);
        Unwrap(similarlyNamedSibling.CreateStore().Persist(siblingEnvelope));
    }

    [Fact]
    public void malformed_plan_store_keys_fail_without_accessing_storage()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var validHash = new string('a', 64);

        Error(store.Load(null!)).Kind.ShouldBe(ChangePlanStoreErrorKind.Missing);
        Error(store.Load(new ChangePlanStoreKey("", Guid.NewGuid(), validHash))).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Missing);
        Error(store.Load(new ChangePlanStoreKey("repository", Guid.Empty, validHash))).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Missing);
        Error(store.Load(new ChangePlanStoreKey("repository", Guid.NewGuid(), "bad"))).Kind
            .ShouldBe(ChangePlanStoreErrorKind.HashMismatch);
        Error(store.Load(new ChangePlanStoreKey("repository", Guid.NewGuid(), validHash.ToUpperInvariant()))).Kind
            .ShouldBe(ChangePlanStoreErrorKind.HashMismatch);
        Directory.Exists(fixture.StoreRoot).ShouldBeFalse();
    }

    [Fact]
    public void valid_keys_for_absent_records_fail_consistently_for_load_and_update()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var persisted = Unwrap(store.Persist(fixture.CreateEnvelope()));
        var absent = persisted with { PlanId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee") };

        Error(store.Load(absent)).Kind.ShouldBe(ChangePlanStoreErrorKind.Missing);
        Error(store.ReleasePin(absent, ChangePlanPinKind.DispatchLease, "dispatch"))
            .Kind.ShouldBe(ChangePlanStoreErrorKind.Missing);
    }

    [Fact]
    public void malformed_store_records_and_changed_envelope_hashes_fail_closed()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        var record = JsonNode.Parse(File.ReadAllBytes(fixture.RecordPath))!.AsObject();
        var envelopeBytes = Convert.FromBase64String(record["envelopeBytes"]!.GetValue<string>());
        var marker = Encoding.UTF8.GetBytes("\"planHash\":\"");
        var markerIndex = envelopeBytes.AsSpan().IndexOf(marker);
        markerIndex.ShouldBeGreaterThanOrEqualTo(0);
        var hashIndex = markerIndex + marker.Length;
        envelopeBytes[hashIndex] = envelopeBytes[hashIndex] == (byte)'0' ? (byte)'1' : (byte)'0';
        record["envelopeBytes"] = Convert.ToBase64String(envelopeBytes);
        File.WriteAllText(fixture.RecordPath, record.ToJsonString());

        var error = Error(store.Load(key));

        error.Kind.ShouldBe(ChangePlanStoreErrorKind.HashMismatch);
        File.Exists(fixture.RecordPath).ShouldBeTrue();
    }

    [Fact]
    public void incompatible_plan_and_store_schemas_are_distinguished()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        var record = JsonNode.Parse(File.ReadAllBytes(fixture.RecordPath))!.AsObject();
        var envelopeBytes = Encoding.UTF8.GetString(
            Convert.FromBase64String(record["envelopeBytes"]!.GetValue<string>()));
        record["envelopeBytes"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(envelopeBytes.Replace(
            ChangePlanSchema.Version,
            "rt.change-plan.v999",
            StringComparison.Ordinal)));
        File.WriteAllText(fixture.RecordPath, record.ToJsonString());

        Error(store.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.SchemaIncompatible);

        record = JsonNode.Parse(File.ReadAllBytes(fixture.RecordPath))!.AsObject();
        record["schemaVersion"] = 999;
        File.WriteAllText(fixture.RecordPath, record.ToJsonString());
        Error(store.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.SchemaIncompatible);
    }

    [Fact]
    public void unreadable_json_outer_identity_and_retention_corruption_are_distinguished()
    {
        using (var malformed = new StoreFixture())
        {
            var store = malformed.CreateStore();
            var key = Unwrap(store.Persist(malformed.CreateEnvelope()));
            File.WriteAllText(malformed.RecordPath, "{");
            Error(store.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.Unreadable);
        }

        using (var identity = new StoreFixture())
        {
            var store = identity.CreateStore();
            var key = Unwrap(store.Persist(identity.CreateEnvelope()));
            var record = JsonNode.Parse(File.ReadAllBytes(identity.RecordPath))!.AsObject();
            record["repositoryId"] = "different-repository";
            File.WriteAllText(identity.RecordPath, record.ToJsonString());
            Error(store.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.HashMismatch);
        }

        using (var retention = new StoreFixture())
        {
            var store = retention.CreateStore();
            var key = Unwrap(store.Persist(retention.CreateEnvelope()));
            var record = JsonNode.Parse(File.ReadAllBytes(retention.RecordPath))!.AsObject();
            record["retainUntilUtc"] = retention.Now + TimeSpan.FromMinutes(1);
            File.WriteAllText(retention.RecordPath, record.ToJsonString());
            var error = Error(store.Load(key));
            error.Kind.ShouldBe(ChangePlanStoreErrorKind.Unreadable);
            error.Message.ShouldContain("retention boundary");
        }
    }

    [Fact]
    public void every_required_store_record_field_is_validated_before_use()
    {
        Action<StoreFixture, JsonObject>[] corruptions =
        [
            (_, record) => record["repositoryId"] = "",
            (_, record) => record["planId"] = "",
            (_, record) => record["planHash"] = "",
            (_, record) => record["envelopeBytes"] = null,
            (_, record) => record["envelopeBytes"] = Convert.ToBase64String([]),
            (fixture, record) => record["envelopeBytes"] = Convert.ToBase64String(
                new byte[checked((int)fixture.Options.MaximumPlanBytes + 1)]),
            (_, record) => record["pins"] = null,
            (fixture, record) => record["pins"] = new JsonArray(
                Enumerable.Range(0, fixture.Options.MaximumPinsPerPlan + 1)
                    .Select(index => (JsonNode?)CreatePinNode(
                        ChangePlanPinKind.DispatchLease,
                        $"dispatch-{index}",
                        InitialNow,
                        InitialNow + TimeSpan.FromMinutes(10)))
                    .ToArray()),
            (_, record) => record["persistedAtUtc"] = "2030-01-02T04:04:05+01:00",
            (_, record) => record["retainUntilUtc"] = "2030-01-02T05:04:05+01:00",
            (_, record) => record["retainUntilUtc"] = InitialNow - TimeSpan.FromTicks(1)
        ];

        foreach (var corrupt in corruptions)
            AssertCorruptRecordIsRetained(corrupt);
    }

    [Fact]
    public void every_invalid_pin_shape_is_rejected_and_exact_limits_are_accepted()
    {
        Action<StoreFixture, JsonObject>[] corruptions =
        [
            (_, record) => record["pins"] = new JsonArray((JsonNode?)null),
            (_, record) => record["pins"] = new JsonArray(CreatePinNode(
                (ChangePlanPinKind)999,
                "dispatch",
                InitialNow,
                InitialNow + TimeSpan.FromMinutes(10))),
            (_, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.DispatchLease,
                "",
                InitialNow,
                InitialNow + TimeSpan.FromMinutes(10))),
            (fixture, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.DispatchLease,
                new string('x', fixture.Options.MaximumPinReferenceBytes + 1),
                InitialNow,
                InitialNow + TimeSpan.FromMinutes(10))),
            (_, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.DispatchLease,
                "dispatch",
                new DateTimeOffset(2030, 1, 2, 4, 4, 5, TimeSpan.FromHours(1)),
                InitialNow + TimeSpan.FromMinutes(10))),
            (_, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.DispatchLease,
                "dispatch",
                InitialNow,
                expiresAtUtc: null)),
            (_, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.DispatchLease,
                "dispatch",
                InitialNow,
                new DateTimeOffset(2030, 1, 2, 4, 14, 5, TimeSpan.FromHours(1)))),
            (_, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.DispatchLease,
                "dispatch",
                InitialNow,
                InitialNow)),
            (_, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.OperationLedger,
                "operation",
                InitialNow,
                InitialNow + TimeSpan.FromMinutes(10),
                "dispatch")),
            (_, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.OperationLedger,
                "operation",
                InitialNow,
                expiresAtUtc: null)),
            (fixture, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.OperationLedger,
                "operation",
                InitialNow,
                expiresAtUtc: null,
                adoptedFromDispatchLeaseId: new string(
                    'x',
                    fixture.Options.MaximumPinReferenceBytes + 1))),
            (_, record) => record["pins"] = new JsonArray(CreatePinNode(
                ChangePlanPinKind.DispatchLease,
                "dispatch",
                InitialNow,
                InitialNow + TimeSpan.FromMinutes(10),
                "unexpected-source")),
            (_, record) => record["pins"] = new JsonArray(
                CreatePinNode(ChangePlanPinKind.OperationLedger, "operation", InitialNow, null, "dispatch"),
                CreatePinNode(ChangePlanPinKind.OperationLedger, "operation", InitialNow, null, "dispatch"))
        ];

        foreach (var corrupt in corruptions)
            AssertCorruptRecordIsRetained(corrupt);

        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        var record = JsonNode.Parse(File.ReadAllBytes(fixture.RecordPath))!.AsObject();
        record["pins"] = new JsonArray(CreatePinNode(
            ChangePlanPinKind.DispatchLease,
            new string('x', fixture.Options.MaximumPinReferenceBytes),
            InitialNow,
            InitialNow + TimeSpan.FromMinutes(10)));
        File.WriteAllText(fixture.RecordPath, record.ToJsonString());

        Unwrap(store.Load(key)).Pins.ShouldHaveSingleItem();
    }

    [Fact]
    public void missing_expired_and_overlong_plans_have_distinct_no_effect_failures()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var envelope = fixture.CreateEnvelope();
        var key = new ChangePlanStoreKey(
            envelope.Payload.Repository.RepositoryId,
            envelope.Payload.PlanId,
            envelope.PlanHash);

        Error(store.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.Missing);
        Unwrap(store.Persist(envelope));
        fixture.Now = envelope.Payload.ExpiresAtUtc;
        Error(store.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.Expired);
        Error(store.PinDispatchLease(
                key,
                "late-dispatch",
                fixture.Now + TimeSpan.FromMinutes(10)))
            .Kind.ShouldBe(ChangePlanStoreErrorKind.Expired);

        var overlong = fixture.CreateEnvelope(
            planId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            expiresAtUtc: InitialNow + fixture.Options.MaximumPlanLifetime + TimeSpan.FromMinutes(1));
        Error(fixture.CreateStore().Persist(overlong)).Kind.ShouldBe(ChangePlanStoreErrorKind.CapacityExceeded);
    }

    [Fact]
    public void invalid_expired_and_oversized_envelopes_fail_before_publication()
    {
        using var invalidFixture = new StoreFixture();
        var valid = invalidFixture.CreateEnvelope();
        var invalid = ChangePlanEnvelope.FromStored(
            valid.Payload,
            new string('0', 64),
            valid.CanonicalPayloadBytes);
        Error(invalidFixture.CreateStore().Persist(invalid)).Kind
            .ShouldBe(ChangePlanStoreErrorKind.HashMismatch);
        Directory.Exists(invalidFixture.StoreRoot).ShouldBeFalse();

        using var expiredFixture = new StoreFixture();
        var expired = expiredFixture.CreateEnvelope(
            createdAtUtc: expiredFixture.Now - TimeSpan.FromMinutes(20),
            expiresAtUtc: expiredFixture.Now);
        Error(expiredFixture.CreateStore().Persist(expired)).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Expired);
        Directory.Exists(expiredFixture.StoreRoot).ShouldBeFalse();

        using var oversizedFixture = new StoreFixture();
        var oversized = oversizedFixture.CreateEnvelope();
        var options = oversizedFixture.Options with
        {
            MaximumPlanBytes = oversized.SerializeEnvelope().LongLength - 1
        };
        Error(oversizedFixture.CreateStore(options).Persist(oversized)).Kind
            .ShouldBe(ChangePlanStoreErrorKind.CapacityExceeded);
        Directory.Exists(oversizedFixture.StoreRoot).ShouldBeFalse();
    }

    [Fact]
    public void plan_time_and_envelope_size_limits_accept_their_exact_boundaries()
    {
        using var fixture = new StoreFixture();
        var createdAt = fixture.Now;
        var boundaryEnvelope = fixture.CreateEnvelope(
            createdAtUtc: createdAt,
            expiresAtUtc: createdAt + fixture.Options.MaximumPlanLifetime);
        var boundaryOptions = fixture.Options with
        {
            MaximumPlanBytes = boundaryEnvelope.SerializeEnvelope().LongLength
        };

        var boundaryStore = fixture.CreateStore(boundaryOptions);
        var boundaryKey = Unwrap(boundaryStore.Persist(boundaryEnvelope));
        Unwrap(boundaryStore.Load(boundaryKey)).EnvelopeBytes
            .AsSpan().SequenceEqual(boundaryEnvelope.SerializeEnvelope()).ShouldBeTrue();

        using var futureFixture = new StoreFixture();
        var future = futureFixture.CreateEnvelope(
            createdAtUtc: futureFixture.Now + TimeSpan.FromTicks(1),
            expiresAtUtc: futureFixture.Now + TimeSpan.FromMinutes(20));
        Error(futureFixture.CreateStore().Persist(future)).Kind.ShouldBe(ChangePlanStoreErrorKind.Conflict);
    }

    [Fact]
    public void retention_uses_whichever_valid_boundary_is_later()
    {
        using var expiryFixture = new StoreFixture(unreferencedRetention: TimeSpan.Zero);
        var expiryEnvelope = expiryFixture.CreateEnvelope();
        var expiryStored = Unwrap(expiryFixture.CreateStore().Persist(expiryEnvelope));
        Unwrap(expiryFixture.CreateStore().Load(expiryStored)).RetainUntilUtc
            .ShouldBe(expiryEnvelope.Payload.ExpiresAtUtc);

        using var retentionFixture = new StoreFixture(unreferencedRetention: TimeSpan.FromHours(2));
        var retentionEnvelope = retentionFixture.CreateEnvelope();
        var retentionStored = Unwrap(retentionFixture.CreateStore().Persist(retentionEnvelope));
        Unwrap(retentionFixture.CreateStore().Load(retentionStored)).RetainUntilUtc
            .ShouldBe(retentionFixture.Now + TimeSpan.FromHours(2));
    }

    [Fact]
    public void plan_count_capacity_is_reserved_before_an_extra_record_is_written()
    {
        using var fixture = new StoreFixture(maximumPlanCount: 1);
        var store = fixture.CreateStore();
        Unwrap(store.Persist(fixture.CreateEnvelope()));

        var second = fixture.CreateEnvelope(
            planId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Error(store.Persist(second)).Kind.ShouldBe(ChangePlanStoreErrorKind.CapacityExceeded);
        Directory.EnumerateFiles(fixture.StoreRoot, "*.plan", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void total_store_bytes_accept_the_exact_record_boundary_and_reject_an_extra_plan()
    {
        long recordBytes;
        using (var probe = new StoreFixture())
        {
            Unwrap(probe.CreateStore().Persist(probe.CreateEnvelope()));
            recordBytes = new FileInfo(probe.RecordPath).Length;
        }

        using var fixture = new StoreFixture();
        var envelope = fixture.CreateEnvelope();
        var options = fixture.Options with
        {
            MaximumPlanBytes = envelope.SerializeEnvelope().LongLength,
            MaximumStoreBytes = recordBytes
        };
        var store = fixture.CreateStore(options);

        Unwrap(store.Persist(envelope));
        var second = fixture.CreateEnvelope(
            planId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Error(store.Persist(second)).Kind.ShouldBe(ChangePlanStoreErrorKind.CapacityExceeded);
        new FileInfo(fixture.RecordPath).Length.ShouldBe(recordBytes);
    }

    [Fact]
    public void total_store_bytes_reserve_the_exact_pin_update_delta()
    {
        long pinnedRecordBytes;
        using (var probe = new StoreFixture())
        {
            var probeStore = probe.CreateStore();
            var probeKey = Unwrap(probeStore.Persist(probe.CreateEnvelope()));
            Unwrap(probeStore.PinDispatchLease(
                probeKey,
                "dispatch-1",
                probe.Now + TimeSpan.FromMinutes(10)));
            pinnedRecordBytes = new FileInfo(probe.RecordPath).Length;
        }

        using var fixture = new StoreFixture();
        var envelope = fixture.CreateEnvelope();
        var options = fixture.Options with
        {
            MaximumPlanBytes = envelope.SerializeEnvelope().LongLength,
            MaximumStoreBytes = pinnedRecordBytes
        };
        var store = fixture.CreateStore(options);
        var key = Unwrap(store.Persist(envelope));

        Unwrap(store.PinDispatchLease(
            key,
            "dispatch-1",
            fixture.Now + TimeSpan.FromMinutes(10)));
        Error(store.PinDispatchLease(
                key,
                "dispatch-2",
                fixture.Now + TimeSpan.FromMinutes(10)))
            .Kind.ShouldBe(ChangePlanStoreErrorKind.CapacityExceeded);
        new FileInfo(fixture.RecordPath).Length.ShouldBe(pinnedRecordBytes);
    }

    [Fact]
    public void pin_count_and_reference_bytes_are_bounded_before_state_changes()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        for (var index = 0; index < fixture.Options.MaximumPinsPerPlan; index++)
        {
            Unwrap(store.PinDispatchLease(
                key,
                $"dispatch-{index}",
                InitialNow + TimeSpan.FromMinutes(30)));
        }

        Error(store.PinDispatchLease(
                key,
                "dispatch-over-capacity",
                InitialNow + TimeSpan.FromMinutes(30)))
            .Kind.ShouldBe(ChangePlanStoreErrorKind.CapacityExceeded);
        var overlongReference = Should.Throw<ArgumentOutOfRangeException>(() => store.PinDispatchLease(
            key,
            new string('x', fixture.Options.MaximumPinReferenceBytes + 1),
            InitialNow + TimeSpan.FromMinutes(30)));
        overlongReference.Message.ShouldContain("pin reference");
        var overlongLease = Should.Throw<ArgumentOutOfRangeException>(() => store.PinDispatchLease(
            key,
            "too-long-lived",
            InitialNow + fixture.Options.MaximumDispatchLeaseLifetime + TimeSpan.FromSeconds(1)));
        overlongLease.Message.ShouldContain("lifetime limit");
        Unwrap(store.Load(key)).Pins.Length.ShouldBe(fixture.Options.MaximumPinsPerPlan);
    }

    [Fact]
    public void public_pin_operations_validate_required_and_exact_length_references()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        var expiry = fixture.Now + TimeSpan.FromMinutes(10);

        Should.Throw<ArgumentException>(() => store.PinDispatchLease(key, "", expiry))
            .ParamName.ShouldBe("dispatchLeaseId");
        Should.Throw<ArgumentException>(() => store.AdoptDispatchLease(key, "", "operation"))
            .ParamName.ShouldBe("dispatchLeaseId");
        Should.Throw<ArgumentException>(() => store.AdoptDispatchLease(key, "dispatch", ""))
            .ParamName.ShouldBe("operationId");
        Should.Throw<ArgumentException>(() => store.ReleasePin(
                key,
                ChangePlanPinKind.DispatchLease,
                ""))
            .ParamName.ShouldBe("referenceId");

        var overlong = new string('x', fixture.Options.MaximumPinReferenceBytes + 1);
        Should.Throw<ArgumentOutOfRangeException>(() => store.AdoptDispatchLease(
            key,
            overlong,
            "operation"));
        Should.Throw<ArgumentOutOfRangeException>(() => store.AdoptDispatchLease(
            key,
            "dispatch",
            overlong));
        Should.Throw<ArgumentOutOfRangeException>(() => store.ReleasePin(
            key,
            ChangePlanPinKind.DispatchLease,
            overlong));

        var exact = new string('x', fixture.Options.MaximumPinReferenceBytes);
        Unwrap(store.PinDispatchLease(key, exact, expiry));
        var adopted = Unwrap(store.AdoptDispatchLease(key, exact, "operation"));
        adopted.Pins.ShouldHaveSingleItem().AdoptedFromDispatchLeaseId.ShouldBe(exact);
        Unwrap(store.ReleasePin(key, ChangePlanPinKind.OperationLedger, "operation"));
    }

    [Fact]
    public void dispatch_lease_boundaries_and_idempotency_are_exact()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));

        var expiredLease = Should.Throw<ArgumentOutOfRangeException>(() => store.PinDispatchLease(
            key,
            "already-expired",
            fixture.Now));
        expiredLease.Message.ShouldContain("future");
        var exactExpiry = fixture.Now + fixture.Options.MaximumDispatchLeaseLifetime;
        Unwrap(store.PinDispatchLease(key, "dispatch-1", exactExpiry));
        Unwrap(store.PinDispatchLease(key, "dispatch-1", exactExpiry));
        Error(store.PinDispatchLease(key, "dispatch-1", exactExpiry - TimeSpan.FromTicks(1))).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Conflict);
        Unwrap(store.Load(key)).Pins.ShouldHaveSingleItem().ExpiresAtUtc.ShouldBe(exactExpiry);
    }

    [Fact]
    public void dispatch_pin_rechecks_expiry_at_the_locked_transition()
    {
        using var fixture = new StoreFixture();
        var key = Unwrap(fixture.CreateStore().Persist(fixture.CreateEnvelope()));
        var expiry = fixture.Now + TimeSpan.FromMinutes(1);
        var clockReads = 0;
        var store = fixture.CreateStore(utcNow: () => Interlocked.Increment(ref clockReads) == 1
            ? fixture.Now
            : expiry);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            store.PinDispatchLease(key, "dispatch-crossed-expiry", expiry));
        Unwrap(fixture.CreateStore().Load(key)).Pins.ShouldBeEmpty();
    }

    [Fact]
    public void dispatch_pin_rechecks_maximum_lifetime_after_clock_rollback()
    {
        using var fixture = new StoreFixture();
        var key = Unwrap(fixture.CreateStore().Persist(fixture.CreateEnvelope()));
        var expiry = fixture.Now + fixture.Options.MaximumDispatchLeaseLifetime;
        var clockReads = 0;
        var store = fixture.CreateStore(utcNow: () => Interlocked.Increment(ref clockReads) == 1
            ? fixture.Now
            : fixture.Now - TimeSpan.FromTicks(1));

        Should.Throw<ArgumentOutOfRangeException>(() =>
            store.PinDispatchLease(key, "dispatch-clock-rollback", expiry));
        Unwrap(fixture.CreateStore().Load(key)).Pins.ShouldBeEmpty();
    }

    [Fact]
    public void corrupt_pin_metadata_fails_closed_and_is_not_garbage_collected()
    {
        using var fixture = new StoreFixture(unreferencedRetention: TimeSpan.Zero);
        var store = fixture.CreateStore();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(store.Persist(envelope));
        var recordPath = fixture.RecordPath;
        var record = JsonNode.Parse(File.ReadAllBytes(recordPath))!.AsObject();
        record["pins"] = new JsonArray
        {
            new JsonObject
            {
                ["kind"] = 999,
                ["referenceId"] = "forged",
                ["createdAtUtc"] = InitialNow,
                ["expiresAtUtc"] = null
            }
        };
        File.WriteAllText(recordPath, record.ToJsonString());
        fixture.Now = InitialNow + TimeSpan.FromHours(1);

        Error(store.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.Unreadable);
        var collection = store.CollectGarbage();
        collection.FailedPlans.ShouldBe(1);
        collection.DeletedPlans.ShouldBe(0);
        File.Exists(recordPath).ShouldBeTrue();
    }

    [Fact]
    public void dispatch_pin_is_atomically_adopted_and_retained_until_operation_release()
    {
        using var fixture = new StoreFixture(unreferencedRetention: TimeSpan.Zero);
        var store = fixture.CreateStore();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(store.Persist(envelope));
        var recordPath = fixture.RecordPath;
        Unwrap(store.PinDispatchLease(key, "dispatch-1", InitialNow + TimeSpan.FromMinutes(30)));
        var adopted = Unwrap(store.AdoptDispatchLease(key, "dispatch-1", "operation-1"));

        adopted.Pins.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            pin => pin.Kind.ShouldBe(ChangePlanPinKind.OperationLedger),
            pin => pin.ReferenceId.ShouldBe("operation-1"),
            pin => pin.ExpiresAtUtc.ShouldBeNull(),
            pin => pin.AdoptedFromDispatchLeaseId.ShouldBe("dispatch-1"));

        fixture.Now = InitialNow + TimeSpan.FromHours(2);
        var retained = store.CollectGarbage();
        retained.DeletedPlans.ShouldBe(0);
        retained.RetainedPlans.ShouldBe(1);
        File.Exists(recordPath).ShouldBeTrue();

        Unwrap(store.ReleasePin(key, ChangePlanPinKind.OperationLedger, "operation-1"));
        var deleted = store.CollectGarbage();
        deleted.DeletedPlans.ShouldBe(1);
        File.Exists(recordPath).ShouldBeFalse();
    }

    [Fact]
    public void an_operation_pin_cannot_absorb_a_different_dispatch_lease()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        Unwrap(store.PinDispatchLease(key, "dispatch-1", InitialNow + TimeSpan.FromMinutes(30)));
        Unwrap(store.AdoptDispatchLease(key, "dispatch-1", "operation-1"));
        Unwrap(store.PinDispatchLease(key, "dispatch-2", InitialNow + TimeSpan.FromMinutes(30)));

        Error(store.AdoptDispatchLease(key, "dispatch-2", "operation-1")).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Conflict);
        Unwrap(store.Load(key)).Pins.Length.ShouldBe(2);
    }

    [Fact]
    public void adoption_matches_operation_identity_within_the_operation_pin_kind()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        var expiry = fixture.Now + TimeSpan.FromMinutes(10);
        Unwrap(store.PinDispatchLease(key, "dispatch-1", expiry));
        Unwrap(store.PinDispatchLease(key, "operation-1", expiry));

        var adopted = Unwrap(store.AdoptDispatchLease(key, "dispatch-1", "operation-1"));

        adopted.Pins.Select(pin => (pin.Kind, pin.ReferenceId)).ShouldBe(
        [
            (ChangePlanPinKind.DispatchLease, "operation-1"),
            (ChangePlanPinKind.OperationLedger, "operation-1")
        ]);
    }

    [Fact]
    public void adoption_retry_is_idempotent_and_missing_or_expired_dispatch_is_rejected()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        Error(store.AdoptDispatchLease(key, "missing", "operation-1")).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Conflict);

        Unwrap(store.PinDispatchLease(key, "dispatch-1", fixture.Now + TimeSpan.FromMinutes(10)));
        Unwrap(store.AdoptDispatchLease(key, "dispatch-1", "operation-1"));
        var replay = Unwrap(store.AdoptDispatchLease(key, "dispatch-1", "operation-1"));
        replay.Pins.ShouldHaveSingleItem().Kind.ShouldBe(ChangePlanPinKind.OperationLedger);
        Error(store.AdoptDispatchLease(key, "unrelated-missing", "operation-1")).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Conflict);
        Unwrap(store.PinDispatchLease(key, "dispatch-1", fixture.Now + TimeSpan.FromMinutes(10)));
        Error(store.AdoptDispatchLease(key, "dispatch-1", "operation-1")).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Conflict);
        Unwrap(store.ReleasePin(key, ChangePlanPinKind.DispatchLease, "dispatch-1"));

        Unwrap(store.PinDispatchLease(key, "dispatch-2", fixture.Now + TimeSpan.FromMinutes(10)));
        fixture.Now += TimeSpan.FromMinutes(10);
        Error(store.AdoptDispatchLease(key, "dispatch-2", "operation-2")).Kind
            .ShouldBe(ChangePlanStoreErrorKind.Conflict);
    }

    [Fact]
    public void adoption_retry_remains_available_after_plan_expiry_while_its_dispatch_lease_is_live()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(store.Persist(envelope));
        Unwrap(store.PinDispatchLease(
            key,
            "dispatch-1",
            envelope.Payload.ExpiresAtUtc + TimeSpan.FromMinutes(10)));
        fixture.Now = envelope.Payload.ExpiresAtUtc;

        var adopted = Unwrap(store.AdoptDispatchLease(key, "dispatch-1", "operation-1"));

        adopted.Pins.ShouldHaveSingleItem().Kind.ShouldBe(ChangePlanPinKind.OperationLedger);
    }

    [Fact]
    public async Task adoption_rechecks_lease_expiry_after_lock_contention()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        var leaseExpiry = fixture.Now + TimeSpan.FromMinutes(10);
        Unwrap(store.PinDispatchLease(key, "dispatch-1", leaseExpiry));
        using var reachedProtectionCheck = new ManualResetEventSlim();
        var contendedStore = fixture.CreateStore(
            protectionAuthority: new SignalingProtectionAuthority(
                fixture.StoreRoot,
                reachedProtectionCheck));
        using var lockStream = new FileStream(
            Path.Combine(fixture.StoreRoot, ".store.lock"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var adoption = Task.Run(() =>
            contendedStore.AdoptDispatchLease(key, "dispatch-1", "operation-1"));
        reachedProtectionCheck.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        fixture.Now = leaseExpiry;
        lockStream.Dispose();

        Error(await adoption).Kind.ShouldBe(ChangePlanStoreErrorKind.Conflict);
        Unwrap(store.Load(key)).Pins.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            pin => pin.Kind.ShouldBe(ChangePlanPinKind.DispatchLease),
            pin => pin.ReferenceId.ShouldBe("dispatch-1"));
    }

    [Fact]
    public void garbage_collection_reports_an_incomplete_protected_root_without_mutation()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new StoreFixture();
        Directory.CreateDirectory(fixture.StoreRoot);
        File.SetUnixFileMode(
            fixture.StoreRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var result = fixture.CreateStore().CollectGarbage();

        result.ShouldBe(new ChangePlanGarbageCollectionResult(0, 0, 0, 1));
        Directory.Exists(Path.Combine(fixture.StoreRoot, "plans")).ShouldBeFalse();
    }

    [Fact]
    public void release_removes_only_the_exact_pin_and_preserves_canonical_order()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        Unwrap(store.PinDispatchLease(key, "dispatch-z", fixture.Now + TimeSpan.FromMinutes(10)));
        Unwrap(store.PinDispatchLease(key, "dispatch-a", fixture.Now + TimeSpan.FromMinutes(10)));
        Unwrap(store.AdoptDispatchLease(key, "dispatch-z", "operation-z"));
        Unwrap(store.PinDispatchLease(key, "operation-z", fixture.Now + TimeSpan.FromMinutes(10)));

        var beforeRelease = Unwrap(store.Load(key));
        beforeRelease.Pins.Select(pin => (pin.Kind, pin.ReferenceId)).ShouldBe(
        [
            (ChangePlanPinKind.DispatchLease, "dispatch-a"),
            (ChangePlanPinKind.DispatchLease, "operation-z"),
            (ChangePlanPinKind.OperationLedger, "operation-z")
        ]);

        var afterRelease = Unwrap(store.ReleasePin(
            key,
            ChangePlanPinKind.OperationLedger,
            "operation-z"));
        afterRelease.Pins.Select(pin => (pin.Kind, pin.ReferenceId)).ShouldBe(
        [
            (ChangePlanPinKind.DispatchLease, "dispatch-a"),
            (ChangePlanPinKind.DispatchLease, "operation-z")
        ]);
    }

    [Fact]
    public void expired_unadopted_dispatch_pins_and_crash_tombstones_are_collected()
    {
        using var fixture = new StoreFixture(unreferencedRetention: TimeSpan.Zero);
        var store = fixture.CreateStore();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(store.Persist(envelope));
        var recordPath = fixture.RecordPath;
        Unwrap(store.PinDispatchLease(key, "dispatch-1", InitialNow + TimeSpan.FromMinutes(30)));
        var tombstone = $"{recordPath}.gc-interrupted";
        File.WriteAllText(tombstone, "obsolete");
        var temporaryFile = Path.Combine(
            Path.GetDirectoryName(recordPath)!,
            ".interrupted.plan.tmp");
        File.WriteAllText(temporaryFile, "incomplete");
        fixture.Now = InitialNow + TimeSpan.FromHours(1);

        var result = store.CollectGarbage();

        result.ReleasedPins.ShouldBe(1);
        result.DeletedPlans.ShouldBe(1);
        File.Exists(tombstone).ShouldBeFalse();
        File.Exists(temporaryFile).ShouldBeFalse();
        File.Exists(recordPath).ShouldBeFalse();
        Directory.EnumerateFiles(fixture.StoreRoot, "*.gc-*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void garbage_collection_ignores_non_record_files_under_the_plan_tree()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        Unwrap(store.Persist(fixture.CreateEnvelope()));
        var foreignPath = Path.Combine(Path.GetDirectoryName(fixture.RecordPath)!, "foreign.bin");
        File.WriteAllText(foreignPath, "not a plan record");

        var result = store.CollectGarbage();

        result.FailedPlans.ShouldBe(0);
        result.RetainedPlans.ShouldBe(1);
        File.Exists(foreignPath).ShouldBeTrue();
    }

    private static void AssertCorruptRecordIsRetained(Action<StoreFixture, JsonObject> corrupt)
    {
        using var fixture = new StoreFixture(unreferencedRetention: TimeSpan.Zero);
        var store = fixture.CreateStore();
        var key = Unwrap(store.Persist(fixture.CreateEnvelope()));
        var record = JsonNode.Parse(File.ReadAllBytes(fixture.RecordPath))!.AsObject();
        corrupt(fixture, record);
        File.WriteAllText(fixture.RecordPath, record.ToJsonString());

        Error(store.Load(key)).Kind.ShouldBe(ChangePlanStoreErrorKind.Unreadable);
        fixture.Now += TimeSpan.FromHours(1);
        var collection = store.CollectGarbage();
        collection.FailedPlans.ShouldBe(1);
        collection.DeletedPlans.ShouldBe(0);
        File.Exists(fixture.RecordPath).ShouldBeTrue();
    }

    private static JsonObject CreatePinNode(
        ChangePlanPinKind kind,
        string referenceId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc,
        string? adoptedFromDispatchLeaseId = null) => new()
        {
            ["kind"] = (int)kind,
            ["referenceId"] = referenceId,
            ["createdAtUtc"] = createdAtUtc,
            ["expiresAtUtc"] = expiresAtUtc,
            ["adoptedFromDispatchLeaseId"] = adoptedFromDispatchLeaseId
        };

    private static T Unwrap<T>(Result<T, ChangePlanStoreError> result) => result.Match(
        value => value,
        error => throw new InvalidOperationException(error.Message));

    private static ChangePlanStoreError Error<T>(Result<T, ChangePlanStoreError> result) => result.Match(
        _ => throw new InvalidOperationException("Expected the protected plan-store operation to fail."),
        error =>
        {
            error.Message.ShouldNotBeNullOrWhiteSpace();
            return error;
        });

    private sealed class StoreFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"rt-plan-store-tests-{Guid.NewGuid():N}");

        internal StoreFixture(
            int maximumPlanCount = 32,
            TimeSpan? unreferencedRetention = null)
        {
            RepositoryRoot = Path.Combine(_root, "repository");
            StoreRoot = Path.Combine(_root, "protected-store");
            ControlRoot = Path.Combine(_root, "operation-control");
            Directory.CreateDirectory(Path.Combine(RepositoryRoot, "src"));
            Directory.CreateDirectory(Path.Combine(ControlRoot, "staging"));
            Directory.CreateDirectory(Path.Combine(ControlRoot, "backup"));
            Options = new ChangePlanStoreOptions(
                maximumPlanCount,
                MaximumPlanBytes: 1024 * 1024,
                MaximumStoreBytes: 8 * 1024 * 1024,
                MaximumPinsPerPlan: 8,
                MaximumPinReferenceBytes: 128,
                MaximumDispatchLeaseLifetime: TimeSpan.FromHours(1),
                MaximumPlanLifetime: TimeSpan.FromHours(4),
                UnreferencedRetention: unreferencedRetention ?? TimeSpan.FromHours(1));
        }

        internal DateTimeOffset Now { get; set; } = InitialNow;
        internal string RepositoryRoot { get; }
        internal string StoreRoot { get; }
        internal string ControlRoot { get; }
        internal ChangePlanStoreOptions Options { get; }

        internal string RecordPath => Directory.EnumerateFiles(
            StoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single();

        internal string ExpectedRecordPath(ChangePlanStoreKey key)
        {
            var repositoryDigest = Hash(Encoding.UTF8.GetBytes(key.RepositoryId));
            return Path.Combine(
                StoreRoot,
                "plans",
                repositoryDigest[..2],
                repositoryDigest,
                key.PlanId.ToString("N"),
                $"{key.PlanHash}.plan");
        }

        internal DurableChangePlanStore CreateStore(
            ChangePlanStoreOptions? options = null,
            Func<DateTimeOffset>? utcNow = null,
            IChangePlanStoreProtectionAuthority? protectionAuthority = null,
            IChangePlanStoreDurability? durability = null) => new(
            StoreRoot,
            options ?? Options,
            utcNow ?? (() => Now),
            protectionAuthority ?? new TestProtectionAuthority(StoreRoot),
            durability);

        internal ChangePlanEnvelope CreateEnvelope(
            Guid? planId = null,
            DateTimeOffset? createdAtUtc = null,
            DateTimeOffset? expiresAtUtc = null,
            ChangePlanCoordinationDomain domain = ChangePlanCoordinationDomain.ExclusivePrincipal,
            string? repositoryRoot = null,
            string? resolvedRepositoryRoot = null)
        {
            var actualPlanId = planId ?? Guid.Parse("11111111-2222-3333-4444-555555555555");
            var actualRepositoryRoot = repositoryRoot ?? RepositoryRoot;
            var content = Encoding.UTF8.GetBytes("class Added;\n");
            var sourcePath = "src/Added.cs";
            var sourceResolvedPath = Path.Combine(actualRepositoryRoot, "src", "Added.cs");
            var guard = new ChangePlanGuardRequirement(
                sourcePath,
                ChangePlanGuardKind.ExternalWriteGuard,
                "guard-create-v1");
            var after = new ChangePlanPathState(
                exists: true,
                ChangePlanPathKind.RegularFile,
                Hash(content),
                platformFileIdentity: null,
                linkCount: 1,
                knownHardLinkAliases: [],
                encodingName: "utf-8",
                hasByteOrderMark: false,
                newline: "lf",
                fileMode: "0644",
                executable: false,
                readOnly: false,
                symlinkTarget: null);
            var directoryState = new ChangePlanPathState(
                exists: true,
                ChangePlanPathKind.Directory,
                contentHash: null,
                platformFileIdentity: "device:test;inode:src",
                linkCount: 2,
                knownHardLinkAliases: [],
                encodingName: null,
                hasByteOrderMark: null,
                newline: null,
                fileMode: "0755",
                executable: true,
                readOnly: false,
                symlinkTarget: null);
            var payload = new ChangePlanPayload(
                actualPlanId,
                new ChangePlanRepository(
                    "repository-001",
                    actualRepositoryRoot,
                    resolvedRepositoryRoot ?? actualRepositoryRoot,
                    "device:test;inode:repository",
                    new string('a', 40),
                    new string('b', 64)),
                createdAtUtc ?? InitialNow - TimeSpan.FromMinutes(1),
                expiresAtUtc ?? InitialNow + TimeSpan.FromMinutes(20),
                [new ChangePlanOperation(1, "create", sourcePath, [])],
                [
                    new ChangePlanDependency(
                        "src",
                        Path.Combine(actualRepositoryRoot, "src"),
                        ChangePlanDependencyKind.DirectoryEntry,
                        directoryState,
                        new ChangePlanGuardRequirement(
                            "src",
                            ChangePlanGuardKind.ExternalWriteGuard,
                            "guard-directory-v1"))
                ],
                [
                    new ChangePlanEffect(
                        1,
                        ChangePlanEffectKind.Create,
                        ChangePlanArtifactKind.RoslynDocument,
                        sourcePath,
                        sourceResolvedPath,
                        destinationPath: null,
                        destinationResolvedPath: null,
                        ChangePlanPathState.Missing,
                        after,
                        destinationBefore: null,
                        destinationAfter: null,
                        originalContent: null,
                        replacementContent: content,
                        [guard])
                ],
                diagnostics: [],
                assumptions: [],
                new ChangePlanConfidence("Proof", "Complete workspace", ["All inputs loaded"]),
                limitations: [],
                new ChangePlanWorkspace(
                    WorkspaceCompletenessState.Complete,
                    Path.Combine(actualRepositoryRoot, "RoslynTools.slnx"),
                    "Debug",
                    ["net10.0"],
                    ".NET SDK 10.0.100",
                    "Microsoft.CodeAnalysis 5.0.0",
                    "InProcess",
                    "Trusted"),
                [new ChangePlanVerificationAction(1, "build", "dotnet build", "success", required: true)],
                new ChangePlanCoordination(
                    domain,
                    ChangePlanCoordinationMode.ExternalWriteGuards,
                    "one private writer",
                    ControlRoot,
                    ControlRoot,
                    "device:test;inode:control",
                    "device:test",
                    Path.Combine(ControlRoot, "staging"),
                    Path.Combine(ControlRoot, "staging"),
                    "device:test;inode:staging",
                    Path.Combine(ControlRoot, "backup"),
                    Path.Combine(ControlRoot, "backup"),
                    "device:test;inode:backup"),
                new ChangePlanPathPolicy(
                    ChangePlanCaseSensitivity.Sensitive,
                    ChangePlanLongPathPolicy.Reject,
                    ChangePlanReadOnlyPolicy.Reject,
                    ChangePlanSymlinkPolicy.Reject,
                    ChangePlanHardLinkPolicy.Reject),
                ChangePlanCleanupPolicy.RetainUntilVerifiedTerminal,
                ChangePlanRecoveryPolicy.RequireDurableJournal);
            return ChangePlanEnvelope.Create(payload);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private static string Hash(byte[] content) =>
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private sealed class TestProtectionAuthority(string expectedStoreRoot)
        : IChangePlanStoreProtectionAuthority
    {
        public ChangePlanStoreError? AcquireForPersistence(
            string storeRootPath,
            ChangePlanRepository repository,
            out IChangePlanStoreProtectionLease? lease) => Acquire(storeRootPath, out lease);

        public ChangePlanStoreError? AcquireExisting(
            string storeRootPath,
            out IChangePlanStoreProtectionLease? lease) => Acquire(storeRootPath, out lease);

        private ChangePlanStoreError? Acquire(
            string storeRootPath,
            out IChangePlanStoreProtectionLease? lease)
        {
            if (string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(storeRootPath)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedStoreRoot)),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                lease = TestProtectionLease.Instance;
                return null;
            }

            lease = null;
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.UnsafeProtection,
                "The test authority rejected an unexpected protected root.");
        }
    }

    private sealed class SignalingProtectionAuthority(
        string expectedStoreRoot,
        ManualResetEventSlim reachedProtectionCheck)
        : IChangePlanStoreProtectionAuthority
    {
        private readonly TestProtectionAuthority _inner = new(expectedStoreRoot);

        public ChangePlanStoreError? AcquireForPersistence(
            string storeRootPath,
            ChangePlanRepository repository,
            out IChangePlanStoreProtectionLease? lease) =>
            _inner.AcquireForPersistence(storeRootPath, repository, out lease);

        public ChangePlanStoreError? AcquireExisting(
            string storeRootPath,
            out IChangePlanStoreProtectionLease? lease)
        {
            var result = _inner.AcquireExisting(storeRootPath, out lease);
            reachedProtectionCheck.Set();
            return result;
        }
    }

    private sealed class ThrowingProtectionAuthority : IChangePlanStoreProtectionAuthority
    {
        public ChangePlanStoreError? AcquireForPersistence(
            string storeRootPath,
            ChangePlanRepository repository,
            out IChangePlanStoreProtectionLease? lease) => throw new IOException("Injected protection failure.");

        public ChangePlanStoreError? AcquireExisting(
            string storeRootPath,
            out IChangePlanStoreProtectionLease? lease) => throw new IOException("Injected protection failure.");
    }

    private sealed class TrackingProtectionAuthority(
        string expectedStoreRoot,
        TrackingProtectionLease trackingLease)
        : IChangePlanStoreProtectionAuthority
    {
        private readonly TestProtectionAuthority _inner = new(expectedStoreRoot);

        public ChangePlanStoreError? AcquireForPersistence(
            string storeRootPath,
            ChangePlanRepository repository,
            out IChangePlanStoreProtectionLease? lease)
        {
            var error = _inner.AcquireForPersistence(storeRootPath, repository, out _);
            lease = error is null ? trackingLease : null;
            return error;
        }

        public ChangePlanStoreError? AcquireExisting(
            string storeRootPath,
            out IChangePlanStoreProtectionLease? lease)
        {
            var error = _inner.AcquireExisting(storeRootPath, out _);
            lease = error is null ? trackingLease : null;
            return error;
        }
    }

    private sealed class ErrorAndLeaseProtectionAuthority(
        ChangePlanStoreError error,
        TrackingProtectionLease trackingLease)
        : IChangePlanStoreProtectionAuthority
    {
        public ChangePlanStoreError? AcquireForPersistence(
            string storeRootPath,
            ChangePlanRepository repository,
            out IChangePlanStoreProtectionLease? lease)
        {
            lease = trackingLease;
            return error;
        }

        public ChangePlanStoreError? AcquireExisting(
            string storeRootPath,
            out IChangePlanStoreProtectionLease? lease)
        {
            lease = trackingLease;
            return error;
        }
    }

    private sealed class LeaseThenThrowingProtectionAuthority(TrackingProtectionLease trackingLease)
        : IChangePlanStoreProtectionAuthority
    {
        public ChangePlanStoreError? AcquireForPersistence(
            string storeRootPath,
            ChangePlanRepository repository,
            out IChangePlanStoreProtectionLease? lease)
        {
            lease = trackingLease;
            throw new IOException("Injected protection failure after lease acquisition.");
        }

        public ChangePlanStoreError? AcquireExisting(
            string storeRootPath,
            out IChangePlanStoreProtectionLease? lease)
        {
            lease = trackingLease;
            throw new IOException("Injected protection failure after lease acquisition.");
        }
    }

    private sealed class MissingLeaseProtectionAuthority : IChangePlanStoreProtectionAuthority
    {
        public ChangePlanStoreError? AcquireForPersistence(
            string storeRootPath,
            ChangePlanRepository repository,
            out IChangePlanStoreProtectionLease? lease)
        {
            lease = null;
            return null;
        }

        public ChangePlanStoreError? AcquireExisting(
            string storeRootPath,
            out IChangePlanStoreProtectionLease? lease)
        {
            lease = null;
            return null;
        }
    }

    private sealed class TrackingProtectionLease : IChangePlanStoreProtectionLease
    {
        internal int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class TestProtectionLease : IChangePlanStoreProtectionLease
    {
        internal static TestProtectionLease Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private class RecordingDurability : IChangePlanStoreDurability
    {
        internal List<string> Events { get; } = [];

        public virtual void FlushFileMetadata(string path)
        {
            Events.Add($"flush-file:{path}");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        public virtual void ReplaceFile(string temporaryPath, string recordPath)
        {
            Events.Add($"replace:{temporaryPath}:{recordPath}");
            File.Move(temporaryPath, recordPath, overwrite: true);
        }

        public virtual void FlushDirectoryMetadata(string directory)
        {
            Events.Add($"flush-directory:{directory}");
        }
    }

    private sealed class LeaseAssertingDurability(TrackingProtectionLease lease) : RecordingDurability
    {
        public override void FlushFileMetadata(string path)
        {
            AssertLeaseIsHeld();
            base.FlushFileMetadata(path);
        }

        public override void ReplaceFile(string temporaryPath, string recordPath)
        {
            AssertLeaseIsHeld();
            base.ReplaceFile(temporaryPath, recordPath);
        }

        public override void FlushDirectoryMetadata(string directory)
        {
            AssertLeaseIsHeld();
            base.FlushDirectoryMetadata(directory);
        }

        private void AssertLeaseIsHeld() => lease.DisposeCount.ShouldBe(0);
    }

    private sealed class FaultAfterReplaceDurability : RecordingDurability
    {
        private bool _recordReplaced;

        public override void ReplaceFile(string temporaryPath, string recordPath)
        {
            base.ReplaceFile(temporaryPath, recordPath);
            _recordReplaced = true;
        }

        public override void FlushDirectoryMetadata(string directory)
        {
            base.FlushDirectoryMetadata(directory);
            if (_recordReplaced)
                throw new IOException("Injected directory-flush failure after record replacement.");
        }
    }
}
