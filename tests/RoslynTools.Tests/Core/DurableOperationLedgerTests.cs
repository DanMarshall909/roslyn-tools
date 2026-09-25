using System.Text;
using System.Text.Json.Nodes;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class DurableOperationLedgerTests
{
    private static readonly DateTimeOffset InitialNow =
        new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void a_new_process_view_observes_the_durable_intake_receipt()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();

        var accepted = Unwrap(fixture.CreateLedger().Receive(request, Principal("principal-001")));
        var loaded = Unwrap(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId));

        accepted.Disposition.ShouldBe(OperationIntakeDisposition.Accepted);
        ShouldMatchOperation(accepted.Operation!, loaded);
        loaded.CurrentState.ShouldBe(OperationLifecycleState.Received);
        loaded.Transitions.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            transition => transition.Sequence.ShouldBe(1),
            transition => transition.State.ShouldBe(OperationLifecycleState.Received),
            transition => transition.CommittedAtUtc.ShouldBe(InitialNow));
        loaded.CanonicalRequestBytes.AsSpan().SequenceEqual(request.CanonicalBytes.AsSpan()).ShouldBeTrue();
        loaded.RequestIdentity.ShouldBe(request.Identity);
        loaded.SubmittingPrincipalId.ShouldBe("principal-001");
    }

    [Fact]
    public void constructor_and_authority_inputs_are_rejected_before_filesystem_access()
    {
        Should.Throw<ArgumentException>(() => new DurableOperationLedger(" "));
        Should.Throw<ArgumentException>(() => Principal("\ud800"));

        using var fixture = new LedgerFixture();
        var ledger = fixture.CreateLedger();
        Should.Throw<ArgumentNullException>(() => ledger.Receive(null!, Principal("principal-001")));
        Should.Throw<ArgumentNullException>(() => ledger.Receive(fixture.CreateRequest(), null!));
        Should.Throw<ArgumentNullException>(() => ledger.Load("repository-001", null!));
        Should.Throw<ArgumentNullException>(() => ledger.AppendTransition(
            "repository-001",
            null!,
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared,
            null));
        Directory.Exists(fixture.LedgerRoot).ShouldBeFalse();
    }

    [Fact]
    public void exact_replay_returns_the_retained_record_without_appending_history()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        var first = Unwrap(ledger.Receive(request, Principal("principal-001")));
        var bytesBeforeReplay = File.ReadAllBytes(fixture.RecordPath(request));
        fixture.Now += TimeSpan.FromMinutes(1);

        var replay = Unwrap(fixture.CreateLedger().Receive(request, Principal("principal-001")));

        replay.Disposition.ShouldBe(OperationIntakeDisposition.Replay);
        ShouldMatchOperation(replay.Operation!, first.Operation!);
        replay.Operation!.Transitions.ShouldHaveSingleItem();
        File.ReadAllBytes(fixture.RecordPath(request)).ShouldBe(bytesBeforeReplay);
    }

    [Fact]
    public void conflicting_reuse_does_not_change_the_canonical_operation()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        var accepted = Unwrap(ledger.Receive(request, Principal("principal-001")));
        var bytesBeforeConflict = File.ReadAllBytes(fixture.RecordPath(request));
        var conflicting = fixture.CreateRequest(planId: Guid.Parse("22222222-3333-4444-5555-666666666666"));

        var conflict = Unwrap(fixture.CreateLedger().Receive(conflicting, Principal("principal-001")));
        var retained = Unwrap(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId));

        conflict.Disposition.ShouldBe(OperationIntakeDisposition.Conflict);
        conflict.Operation.ShouldBeNull();
        ShouldMatchOperation(retained, accepted.Operation!);
        File.ReadAllBytes(fixture.RecordPath(request)).ShouldBe(bytesBeforeConflict);
    }

    [Fact]
    public void another_principal_gets_no_existing_or_conflict_distinction()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        Unwrap(ledger.Receive(request, Principal("principal-001")));

        var exact = Unwrap(ledger.Receive(request, Principal("principal-002")));
        var conflict = Unwrap(ledger.Receive(
            fixture.CreateRequest(planId: Guid.Parse("22222222-3333-4444-5555-666666666666")),
            Principal("principal-002")));

        exact.Disposition.ShouldBe(OperationIntakeDisposition.AccessDenied);
        exact.Operation.ShouldBeNull();
        conflict.ShouldBe(exact);
    }

    [Fact]
    public void lifecycle_transitions_are_durable_and_logically_append_only()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        Unwrap(ledger.Receive(request, Principal("principal-001")));
        fixture.Now += TimeSpan.FromSeconds(1);

        var prepared = Unwrap(ledger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared,
            "plan-loaded"));
        fixture.Now += TimeSpan.FromSeconds(1);
        var applying = Unwrap(fixture.CreateLedger().AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Prepared,
            OperationLifecycleState.Applying,
            "owner-claimed"));
        var restarted = Unwrap(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId));

        applying.CurrentState.ShouldBe(OperationLifecycleState.Applying);
        applying.Transitions.Select(transition => transition.Sequence).ShouldBe([1L, 2L, 3L]);
        applying.Transitions.Select(transition => transition.State).ShouldBe([
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared,
            OperationLifecycleState.Applying
        ]);
        applying.Transitions[1].Evidence.ShouldBe("plan-loaded");
        prepared.Transitions.Length.ShouldBe(2);
        restarted.Transitions.Select(transition => transition.CommittedAtUtc).ShouldBe([
            InitialNow,
            InitialNow + TimeSpan.FromSeconds(1),
            InitialNow + TimeSpan.FromSeconds(2)
        ]);
    }

    [Fact]
    public void an_invalid_transition_is_rejected_without_changing_history()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        Unwrap(ledger.Receive(request, Principal("principal-001")));
        var bytesBefore = File.ReadAllBytes(fixture.RecordPath(request));

        var error = Error(ledger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Received,
            OperationLifecycleState.Applied,
            "invalid-shortcut"));

        error.Kind.ShouldBe(OperationLedgerErrorKind.InvalidTransition);
        File.ReadAllBytes(fixture.RecordPath(request)).ShouldBe(bytesBefore);
    }

    [Fact]
    public void stale_expected_state_is_a_conflict_without_changing_history()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        Unwrap(ledger.Receive(request, Principal("principal-001")));
        var bytesBefore = File.ReadAllBytes(fixture.RecordPath(request));

        var error = Error(ledger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Prepared,
            OperationLifecycleState.Applying,
            "stale-owner"));

        error.Kind.ShouldBe(OperationLedgerErrorKind.Conflict);
        File.ReadAllBytes(fixture.RecordPath(request)).ShouldBe(bytesBefore);
    }

    [Fact]
    public void failed_atomic_publication_preserves_the_last_committed_state()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        Unwrap(ledger.Receive(request, Principal("principal-001")));
        var committedBytes = File.ReadAllBytes(fixture.RecordPath(request));
        var failingLedger = fixture.CreateLedger(
            durability: new FailingReplacementDurability());

        var error = Error(failingLedger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared,
            "plan-loaded"));
        var recovered = Unwrap(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId));

        error.Kind.ShouldBe(OperationLedgerErrorKind.Unreadable);
        recovered.CurrentState.ShouldBe(OperationLifecycleState.Received);
        File.ReadAllBytes(fixture.RecordPath(request)).ShouldBe(committedBytes);
        Directory.EnumerateFiles(fixture.LedgerRoot, "*.tmp", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void an_unproven_ledger_root_fails_closed()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = new DurableOperationLedger(fixture.LedgerRoot, utcNow: () => fixture.Now);

        var error = Error(ledger.Receive(request, Principal("principal-001")));

        error.Kind.ShouldBe(OperationLedgerErrorKind.UnsafeProtection);
        Directory.Exists(fixture.LedgerRoot).ShouldBeFalse();
    }

    [Fact]
    public void a_rejected_protection_proof_disposes_any_provisional_lease()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var lease = new TrackingProtectionLease();
        var authority = new RejectingProtectionAuthority(lease);
        var ledger = new DurableOperationLedger(
            fixture.LedgerRoot,
            utcNow: () => fixture.Now,
            protectionAuthority: authority);

        var error = Error(ledger.Receive(request, Principal("principal-001")));

        error.Kind.ShouldBe(OperationLedgerErrorKind.UnsafeProtection);
        lease.WasDisposed.ShouldBeTrue();
        Directory.Exists(fixture.LedgerRoot).ShouldBeFalse();
    }

    [Fact]
    public void a_throwing_protection_authority_disposes_any_provisional_lease()
    {
        using var fixture = new LedgerFixture();
        var lease = new TrackingProtectionLease();
        var ledger = new DurableOperationLedger(
            fixture.LedgerRoot,
            utcNow: () => fixture.Now,
            protectionAuthority: new ThrowingProtectionAuthority(lease));

        Should.Throw<InvalidOperationException>(() =>
            ledger.Receive(fixture.CreateRequest(), Principal("principal-001")));

        lease.WasDisposed.ShouldBeTrue();
        Directory.Exists(fixture.LedgerRoot).ShouldBeFalse();
    }

    [Fact]
    public void a_successful_protection_result_without_a_lease_fails_closed()
    {
        using var fixture = new LedgerFixture();
        var ledger = new DurableOperationLedger(
            fixture.LedgerRoot,
            utcNow: () => fixture.Now,
            protectionAuthority: new MissingLeaseProtectionAuthority());

        var error = Error(ledger.Receive(fixture.CreateRequest(), Principal("principal-001")));

        error.Kind.ShouldBe(OperationLedgerErrorKind.UnsafeProtection);
        Directory.Exists(fixture.LedgerRoot).ShouldBeFalse();
    }

    [Fact]
    public void a_storage_failure_while_acquiring_read_protection_is_typed_and_evidenced()
    {
        using var fixture = new LedgerFixture();
        var ledger = new DurableOperationLedger(
            fixture.LedgerRoot,
            protectionAuthority: new StorageFailingProtectionAuthority());

        var error = Error(ledger.Load("repository-001", fixture.CreateRequest().OperationId));

        error.Kind.ShouldBe(OperationLedgerErrorKind.Unreadable);
        error.Evidence.ShouldNotBeNull()["exception"].ShouldBe(nameof(IOException));
        Directory.Exists(fixture.LedgerRoot).ShouldBeFalse();
    }

    [Fact]
    public void every_supported_lifecycle_edge_round_trips_through_the_durable_record()
    {
        using var fixture = new LedgerFixture();
        var paths = SupportedLifecyclePaths();

        for (var pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var nonceBytes = new byte[16];
            BitConverter.GetBytes(pathIndex + 1).CopyTo(nonceBytes, 0);
            var request = fixture.CreateRequest(nonce: new Guid(nonceBytes));
            var ledger = fixture.CreateLedger();
            Unwrap(ledger.Receive(request, Principal("principal-001")));
            var current = OperationLifecycleState.Received;

            foreach (var next in paths[pathIndex])
            {
                fixture.Now += TimeSpan.FromTicks(1);
                var updated = Unwrap(ledger.AppendTransition(
                    request.RepositoryId,
                    request.OperationId,
                    current,
                    next,
                    null));
                updated.CurrentState.ShouldBe(next);
                current = next;
            }

            Unwrap(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId))
                .CurrentState.ShouldBe(current);

            if (TerminalStates.Contains(current))
            {
                var error = Error(ledger.AppendTransition(
                    request.RepositoryId,
                    request.OperationId,
                    current,
                    OperationLifecycleState.Prepared,
                    null));
                error.Kind.ShouldBe(OperationLedgerErrorKind.InvalidTransition);
            }
        }
    }

    [Fact]
    public async Task concurrent_exact_intake_creates_one_receipt_and_replays_the_rest()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        using var ready = new CountdownEvent(16);
        using var start = new ManualResetEventSlim();
        var attempts = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            ready.Signal();
            start.Wait();
            return Unwrap(fixture.CreateLedger().Receive(request, Principal("principal-001")));
        })).ToArray();
        ready.Wait();
        start.Set();

        var results = await Task.WhenAll(attempts);
        var retained = Unwrap(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId));

        results.Count(result => result.Disposition == OperationIntakeDisposition.Accepted).ShouldBe(1);
        results.Count(result => result.Disposition == OperationIntakeDisposition.Replay).ShouldBe(15);
        retained.Transitions.ShouldHaveSingleItem();
    }

    [Fact]
    public void corrupt_canonical_request_is_not_overwritten_by_replay()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        Unwrap(ledger.Receive(request, Principal("principal-001")));
        var recordPath = fixture.RecordPath(request);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json["canonicalRequestBase64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("tampered"));
        File.WriteAllText(recordPath, json.ToJsonString());
        var corruptBytes = File.ReadAllBytes(recordPath);

        Error(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
        Error(fixture.CreateLedger().Receive(request, Principal("principal-001"))).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
        File.ReadAllBytes(recordPath).ShouldBe(corruptBytes);
    }

    [Fact]
    public void invalid_json_is_reported_as_evidenced_corruption()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        Unwrap(fixture.CreateLedger().Receive(request, Principal("principal-001")));
        File.WriteAllText(fixture.RecordPath(request), "{");

        var error = Error(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId));

        error.Kind.ShouldBe(OperationLedgerErrorKind.Corrupt);
        error.Evidence.ShouldNotBeNull()["exception"].ShouldBe("JsonException");
    }

    [Fact]
    public void malformed_record_metadata_is_rejected_instead_of_becoming_status()
    {
        var mutations = new (string Name, Action<JsonObject> Apply)[]
        {
            ("schema", json => json["schemaVersion"] = 2),
            ("repository", json => json["repositoryId"] = "repository-002"),
            ("operation", json => json["operationEpoch"] = 43),
            ("request hash", json => json["requestHash"] = new string('b', 64)),
            ("plan projection", json => json["planId"] = "22222222-3333-4444-5555-666666666666"),
            ("base64", json => json["canonicalRequestBase64"] = "not-base64"),
            ("principal", json => json["submittingPrincipalId"] = ""),
            ("current state", json => json["currentState"] = (int)OperationLifecycleState.Applied),
            ("missing history", json => json["transitions"] = null),
            ("sequence", json => json["transitions"]!.AsArray()[0]!["sequence"] = 2),
            ("initial state", json => json["transitions"]!.AsArray()[0]!["state"] =
                (int)OperationLifecycleState.Prepared)
        };

        for (var index = 0; index < mutations.Length; index++)
        {
            using var fixture = new LedgerFixture();
            var nonceBytes = new byte[16];
            BitConverter.GetBytes(index + 1).CopyTo(nonceBytes, 0);
            var request = fixture.CreateRequest(nonce: new Guid(nonceBytes));
            Unwrap(fixture.CreateLedger().Receive(request, Principal("principal-001")));
            var recordPath = fixture.RecordPath(request);
            var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
            mutations[index].Apply(json);
            File.WriteAllText(recordPath, json.ToJsonString());

            var error = Error(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId));

            error.Kind.ShouldBe(OperationLedgerErrorKind.Corrupt, mutations[index].Name);
            if (mutations[index].Name == "base64")
                error.Evidence.ShouldNotBeNull()["exception"].ShouldBe(nameof(FormatException));
        }
    }

    [Fact]
    public void non_contiguous_or_time_reversed_history_is_rejected()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        Unwrap(ledger.Receive(request, Principal("principal-001")));
        fixture.Now += TimeSpan.FromSeconds(1);
        Unwrap(ledger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared,
            null));
        var recordPath = fixture.RecordPath(request);
        var original = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();

        var skippedSequence = JsonNode.Parse(original.ToJsonString())!.AsObject();
        skippedSequence["transitions"]!.AsArray()[1]!["sequence"] = 3;
        File.WriteAllText(recordPath, skippedSequence.ToJsonString());
        Error(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);

        var reversedTime = JsonNode.Parse(original.ToJsonString())!.AsObject();
        reversedTime["transitions"]!.AsArray()[1]!["committedAtUtc"] =
            InitialNow - TimeSpan.FromSeconds(1);
        File.WriteAllText(recordPath, reversedTime.ToJsonString());
        Error(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Fact]
    public void equal_transition_timestamps_are_valid_and_remain_restart_readable()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        Unwrap(ledger.Receive(request, Principal("principal-001")));

        Unwrap(ledger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared,
            null));

        Unwrap(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId))
            .CurrentState.ShouldBe(OperationLifecycleState.Prepared);
    }

    [Fact]
    public void backward_clock_step_cannot_publish_history_that_restart_rejects()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var ledger = fixture.CreateLedger();
        Unwrap(ledger.Receive(request, Principal("principal-001")));
        fixture.Now -= TimeSpan.FromMinutes(1);

        var prepared = Unwrap(ledger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared,
            null));
        var restarted = Unwrap(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId));

        prepared.Transitions[1].CommittedAtUtc.ShouldBe(InitialNow);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        restarted.Transitions[1].CommittedAtUtc.ShouldBe(InitialNow);
    }

    [Fact]
    public void mixed_null_transition_entry_is_typed_corruption_and_is_never_replayed_over()
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        Unwrap(fixture.CreateLedger().Receive(request, Principal("principal-001")));
        var recordPath = fixture.RecordPath(request);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json["transitions"]!.AsArray().Add(null);
        File.WriteAllText(recordPath, json.ToJsonString());
        var corruptBytes = File.ReadAllBytes(recordPath);

        Error(fixture.CreateLedger().Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
        Error(fixture.CreateLedger().Receive(request, Principal("principal-001"))).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
        File.ReadAllBytes(recordPath).ShouldBe(corruptBytes);
    }

    [Fact]
    public void intake_orders_every_injected_durability_boundary()
    {
        using var fixture = new LedgerFixture();
        var durability = new RecordingDurability();
        var request = fixture.CreateRequest();
        var recordPath = fixture.RecordPath(request);
        var recordDirectory = Path.GetDirectoryName(recordPath)!;
        var repositoryDirectory = Path.GetDirectoryName(recordDirectory)!;
        var operationsDirectory = Path.GetDirectoryName(repositoryDirectory)!;
        var sandboxDirectory = Path.GetDirectoryName(fixture.LedgerRoot)!;

        Unwrap(fixture.CreateLedger(durability).Receive(request, Principal("principal-001")));

        durability.Events.Count.ShouldBe(11);
        durability.Events.Take(8).ShouldBe([
            $"directory:{fixture.LedgerRoot}",
            $"directory:{sandboxDirectory}",
            $"directory:{operationsDirectory}",
            $"directory:{fixture.LedgerRoot}",
            $"directory:{repositoryDirectory}",
            $"directory:{operationsDirectory}",
            $"directory:{recordDirectory}",
            $"directory:{repositoryDirectory}"
        ]);
        durability.Events[8].ShouldStartWith($"file:{recordDirectory}{Path.DirectorySeparatorChar}.");
        durability.Events[9].ShouldStartWith($"replace:{recordDirectory}{Path.DirectorySeparatorChar}.");
        durability.Events[9].ShouldEndWith($"->{recordPath}");
        durability.Events[10].ShouldBe($"directory:{recordDirectory}");
    }

    [Fact]
    public void unix_ledger_paths_are_owner_private()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new LedgerFixture();
        Directory.CreateDirectory(Path.Combine(fixture.LedgerRoot, "operations"));
        File.SetUnixFileMode(
            fixture.LedgerRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.SetUnixFileMode(
            Path.Combine(fixture.LedgerRoot, "operations"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var request = fixture.CreateRequest();

        Unwrap(fixture.CreateLedger().Receive(request, Principal("principal-001")));

        var directoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        File.GetUnixFileMode(fixture.LedgerRoot).ShouldBe(directoryMode);
        File.GetUnixFileMode(Path.Combine(fixture.LedgerRoot, "operations")).ShouldBe(directoryMode);
        File.GetUnixFileMode(Path.GetDirectoryName(fixture.RecordPath(request))!).ShouldBe(directoryMode);
        File.GetUnixFileMode(fixture.RecordPath(request)).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.GetUnixFileMode(Path.Combine(fixture.LedgerRoot, ".ledger.lock")).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Theory]
    [InlineData(DurableBoundary.FlushFile, false)]
    [InlineData(DurableBoundary.Replace, false)]
    [InlineData(DurableBoundary.FlushDirectory, true)]
    public void intake_faults_resolve_from_the_last_durable_boundary(
        DurableBoundary boundary,
        bool receiptWasPublished)
    {
        using var fixture = new LedgerFixture();
        var request = fixture.CreateRequest();
        var failingLedger = fixture.CreateLedger(new BoundaryFailingDurability(boundary));

        Error(failingLedger.Receive(request, Principal("principal-001"))).Kind
            .ShouldBe(OperationLedgerErrorKind.Unreadable);
        var observed = fixture.CreateLedger().Load(request.RepositoryId, request.OperationId);

        if (receiptWasPublished)
            Unwrap(observed).CurrentState.ShouldBe(OperationLifecycleState.Received);
        else
            Error(observed).Kind.ShouldBe(OperationLedgerErrorKind.Missing);
    }

    private static T Unwrap<T>(Result<T, OperationLedgerError> result) =>
        result.Match(value => value, error => throw new ShouldAssertException(error.Message));

    private static AuthenticatedPrincipalId Principal(string value) => new(value);

    private static void ShouldMatchOperation(
        DurableOperationRecord actual,
        DurableOperationRecord expected)
    {
        actual.RepositoryId.ShouldBe(expected.RepositoryId);
        actual.OperationId.ShouldBe(expected.OperationId);
        actual.RequestIdentity.ShouldBe(expected.RequestIdentity);
        actual.CanonicalRequestBytes.AsSpan().SequenceEqual(expected.CanonicalRequestBytes.AsSpan())
            .ShouldBeTrue();
        actual.SubmittingPrincipalId.ShouldBe(expected.SubmittingPrincipalId);
        actual.CurrentState.ShouldBe(expected.CurrentState);
        actual.Transitions.ShouldBe(expected.Transitions);
    }

    private static OperationLedgerError Error<T>(Result<T, OperationLedgerError> result) =>
        result.Match(
            _ => throw new ShouldAssertException("Expected an operation-ledger error."),
            error => error);

    private static IReadOnlyList<OperationLifecycleState[]> SupportedLifecyclePaths() =>
    [
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.Applied],
        [OperationLifecycleState.CancelledNoEffect],
        [OperationLifecycleState.RejectedStale],
        [OperationLifecycleState.RejectedPlanUnavailable],
        [OperationLifecycleState.RejectedUnsafeCoordination],
        [OperationLifecycleState.Expired],
        [OperationLifecycleState.Prepared, OperationLifecycleState.CancelledNoEffect],
        [OperationLifecycleState.Prepared, OperationLifecycleState.RejectedStale],
        [OperationLifecycleState.Prepared, OperationLifecycleState.RejectedPlanUnavailable],
        [OperationLifecycleState.Prepared, OperationLifecycleState.RejectedUnsafeCoordination],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Expired],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.RolledBack],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.CancelledNoEffect],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.RejectedStale],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.RejectedUnsafeCoordination],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.Expired],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.OwnerQuiescencePending, OperationLifecycleState.Reconciling, OperationLifecycleState.Applied],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.Reconciling, OperationLifecycleState.RolledBack],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.Reconciling, OperationLifecycleState.CancelledNoEffect],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.Reconciling, OperationLifecycleState.RejectedStale],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.Reconciling, OperationLifecycleState.RejectedPlanUnavailable],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.Reconciling, OperationLifecycleState.RejectedUnsafeCoordination],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.Reconciling, OperationLifecycleState.Expired],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.RecoveryRequired, OperationLifecycleState.RecoveryAcknowledged],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.RecoveryRequired, OperationLifecycleState.Recovering, OperationLifecycleState.RecoveredApplied],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.RecoveryRequired, OperationLifecycleState.Recovering, OperationLifecycleState.RecoveredRolledBack],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.RecoveryRequired, OperationLifecycleState.Recovering, OperationLifecycleState.RecoveryRequired],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.RecoveryRequired, OperationLifecycleState.Recovering, OperationLifecycleState.OwnerQuiescencePending, OperationLifecycleState.Reconciling, OperationLifecycleState.RecoveryRequired],
        [OperationLifecycleState.Prepared, OperationLifecycleState.Applying, OperationLifecycleState.RecoveryRequired, OperationLifecycleState.Recovering, OperationLifecycleState.Reconciling, OperationLifecycleState.RecoveryRequired]
    ];

    private static HashSet<OperationLifecycleState> TerminalStates { get; } =
    [
        OperationLifecycleState.Applied,
        OperationLifecycleState.RolledBack,
        OperationLifecycleState.RecoveredApplied,
        OperationLifecycleState.RecoveredRolledBack,
        OperationLifecycleState.RecoveryAcknowledged,
        OperationLifecycleState.CancelledNoEffect,
        OperationLifecycleState.RejectedStale,
        OperationLifecycleState.RejectedPlanUnavailable,
        OperationLifecycleState.RejectedUnsafeCoordination,
        OperationLifecycleState.Expired
    ];

    private sealed class LedgerFixture : IDisposable
    {
        private static readonly Guid Nonce = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        private static readonly Guid DefaultPlanId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "rt-operation-ledger-tests",
            Guid.NewGuid().ToString("N"));

        internal LedgerFixture() => Directory.CreateDirectory(_root);

        internal DateTimeOffset Now { get; set; } = InitialNow;

        internal string LedgerRoot => Path.Combine(_root, "ledger");

        internal DurableOperationLedger CreateLedger(IOperationLedgerDurability? durability = null) => new(
            LedgerRoot,
            utcNow: () => Now,
            protectionAuthority: new TestProtectionAuthority(LedgerRoot),
            durability: durability);

        internal CanonicalApplyRequest CreateRequest(Guid? planId = null, Guid? nonce = null)
        {
            var claims = new OperationEpochClaims(
                "repository-001",
                "principal-001",
                42,
                8,
                1024 * 1024,
                Now + TimeSpan.FromHours(1));
            return CanonicalApplyRequest.Create(
                "repository-001",
                new OperationId(42, nonce ?? Nonce),
                planId ?? DefaultPlanId,
                new string('a', 64),
                "0123456789abcdef0123456789abcdef01234567",
                claims);
        }

        internal string RecordPath(CanonicalApplyRequest request) =>
            DurableOperationLedger.GetRecordPath(LedgerRoot, request.RepositoryId, request.OperationId);

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestProtectionAuthority(string expectedRoot)
        : IOperationLedgerProtectionAuthority
    {
        public OperationLedgerError? Acquire(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? lease)
        {
            ledgerRootPath.ShouldBe(expectedRoot);
            lease = new TestProtectionLease();
            return null;
        }
    }

    private sealed class TestProtectionLease : IOperationLedgerProtectionLease
    {
        public void Dispose()
        {
        }
    }

    private sealed class TrackingProtectionLease : IOperationLedgerProtectionLease
    {
        internal bool WasDisposed { get; private set; }

        public void Dispose() => WasDisposed = true;
    }

    private sealed class RejectingProtectionAuthority(IOperationLedgerProtectionLease lease)
        : IOperationLedgerProtectionAuthority
    {
        public OperationLedgerError? Acquire(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? protectionLease)
        {
            protectionLease = lease;
            return new OperationLedgerError(
                OperationLedgerErrorKind.UnsafeProtection,
                "Injected rejection.");
        }
    }

    private sealed class ThrowingProtectionAuthority(IOperationLedgerProtectionLease lease)
        : IOperationLedgerProtectionAuthority
    {
        public OperationLedgerError? Acquire(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? protectionLease)
        {
            protectionLease = lease;
            throw new InvalidOperationException("Injected authority failure.");
        }
    }

    private sealed class MissingLeaseProtectionAuthority : IOperationLedgerProtectionAuthority
    {
        public OperationLedgerError? Acquire(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? lease)
        {
            lease = null;
            return null;
        }
    }

    private sealed class StorageFailingProtectionAuthority : IOperationLedgerProtectionAuthority
    {
        public OperationLedgerError? Acquire(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? lease)
        {
            lease = null;
            throw new IOException("Injected protection storage failure.");
        }
    }

    private sealed class FailingReplacementDurability : IOperationLedgerDurability
    {
        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath) =>
            throw new IOException("Injected durable replacement failure.");

        public void FlushDirectoryMetadata(string directory)
        {
        }
    }

    public enum DurableBoundary
    {
        FlushFile,
        Replace,
        FlushDirectory
    }

    private sealed class BoundaryFailingDurability(DurableBoundary boundary)
        : IOperationLedgerDurability
    {
        private bool _recordReplaced;

        public void FlushFileMetadata(string path)
        {
            if (boundary == DurableBoundary.FlushFile)
                throw new IOException("Injected file-flush failure.");
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            if (boundary == DurableBoundary.Replace)
                throw new IOException("Injected replacement failure.");
            File.Move(temporaryPath, recordPath, overwrite: true);
            _recordReplaced = true;
        }

        public void FlushDirectoryMetadata(string directory)
        {
            if (boundary == DurableBoundary.FlushDirectory && _recordReplaced)
                throw new IOException("Injected directory-flush failure.");
        }
    }

    private sealed class RecordingDurability : IOperationLedgerDurability
    {
        internal List<string> Events { get; } = [];

        public void FlushFileMetadata(string path) => Events.Add($"file:{path}");

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            Events.Add($"replace:{temporaryPath}->{recordPath}");
            File.Move(temporaryPath, recordPath, overwrite: true);
        }

        public void FlushDirectoryMetadata(string directory) => Events.Add($"directory:{directory}");
    }
}
