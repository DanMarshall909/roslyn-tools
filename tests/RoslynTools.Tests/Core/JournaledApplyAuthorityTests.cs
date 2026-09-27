using System.Security.Cryptography;
using System.Text;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class JournaledApplyAuthorityTests
{
    [Fact]
    public async Task cancellation_closes_pre_effect_activity_while_an_admitted_write_drains()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        using var reservation = await fixture.Reservations.AcquireAsync(
            fixture.StoredPlan!.Envelope.Payload.Repository,
            CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendTransition(
                request.RepositoryId,
                request.OperationId,
                OperationLifecycleState.Prepared,
                OperationLifecycleState.CancelledNoEffect,
                "Unfenced cancellation")
            .IsErr.ShouldBeTrue();

        var cancelled = fixture.Ledger.CancelBeforeFirstEffect(
            request.RepositoryId,
            request.OperationId);

        cancelled.IsOk.ShouldBeTrue();
        var pending = fixture.LoadOperation(request);
        pending.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        pending.PreEffectActivity.IsClosed.ShouldBeTrue();
        pending.PreEffectActivity.ActiveAdmissions.ShouldContain(admission);
        pending.PreEffectActivity.ControlGeneration.ShouldBeGreaterThan(
            admission.ControlGeneration);
        fixture.Ledger.ConfirmDurableCancelledNoEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        var otherRequest = fixture.Prepare();
        fixture.Ledger.HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                otherRequest.OperationId)
            .Match(value => value, error => throw new IOException(error.Message))
            .ShouldBeTrue();
        fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                reservation).IsErr.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                admission with { Token = Guid.NewGuid() },
                reservation).IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions
            .ShouldContain(admission);
        File.Exists(fixture.TargetPath).ShouldBeFalse();

        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                admission,
                reservation).IsOk.ShouldBeTrue();
        var drained = fixture.LoadOperation(request);
        drained.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        drained.PreEffectActivity.IsClosed.ShouldBeTrue();
        drained.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        fixture.Ledger.ConfirmDurableCancelledNoEffect(
                request.RepositoryId,
                request.OperationId)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                admission,
                reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        fixture.Ledger.HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                otherRequest.OperationId)
            .Match(value => value, error => throw new IOException(error.Message))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task a_new_reservation_cannot_complete_an_earlier_admission()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var first = await fixture.Reservations.AcquireAsync(
            fixture.StoredPlan!.Envelope.Payload.Repository,
            CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                first)
            .Match(value => value, error => throw new IOException(error.Message));
        first.Dispose();
        using var later = await fixture.Reservations.AcquireAsync(
            fixture.StoredPlan.Envelope.Payload.Repository,
            CancellationToken.None);

        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                admission,
                later)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions
            .ShouldContain(admission);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task first_effect_intent_waits_for_pre_effect_activity_and_defeats_late_cancellation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository,
            CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.ClaimApplyOwner(
                request.RepositoryId,
                request.OperationId,
                reservation,
                request.Identity.PlanHash)
            .IsOk.ShouldBeTrue();
        var effect = plan.Envelope.Payload.Effects[0];
        var effectHash = CanonicalChangePlan.ComputeEffectHash(effect);
        var intentHash = OperationEffectIntentIdentity.Compute(
            request.Identity.PlanHash,
            effect.Sequence,
            reservation.FenceToken,
            reservation.FenceEpoch);

        fixture.Ledger.AppendEffectIntent(
                request.RepositoryId,
                request.OperationId,
                effect.Sequence,
                intentHash,
                reservation,
                effectHash,
                OperationGuardTokenSet.Expected(plan.Envelope.Payload))
            .IsErr.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                activity,
                reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendEffectIntent(
                request.RepositoryId,
                request.OperationId,
                effect.Sequence,
                intentHash,
                reservation,
                effectHash,
                OperationGuardTokenSet.Expected(plan.Envelope.Payload))
            .IsOk.ShouldBeTrue();

        fixture.Ledger.CancelBeforeFirstEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        var reloaded = fixture.LoadOperation(request);
        reloaded.CurrentState.ShouldBe(OperationLifecycleState.Applying);
        reloaded.PreEffectActivity.IsClosed.ShouldBeTrue();
        reloaded.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        reloaded.EffectJournal.Single().Kind.ShouldBe(OperationEffectJournalKind.Intent);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cancellation_during_final_preflight_drains_control_activity_without_an_effect()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { CancelDuringPreflight = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        var reloaded = fixture.LoadOperation(request);
        reloaded.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        reloaded.PreEffectActivity.IsClosed.ShouldBeTrue();
        reloaded.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        reloaded.EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public void uncertain_cancellation_durability_blocks_later_mutation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var failingLedger = fixture.CreateLedger(new FailDirectoryFlushFrom(1));

        failingLedger.CancelBeforeFirstEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        var visible = fixture.LoadOperation(request);
        visible.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        visible.EffectJournal.ShouldBeEmpty();
        var otherRequest = fixture.Prepare();

        failingLedger.HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                otherRequest.OperationId)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public void a_visible_cancellation_requires_durable_confirmation_before_success()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var failingLedger = fixture.CreateLedger(new FailDirectoryFlushFrom(1));
        failingLedger.CancelBeforeFirstEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState
            .ShouldBe(OperationLifecycleState.CancelledNoEffect);

        failingLedger.ConfirmDurableCancelledNoEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.ConfirmDurableCancelledNoEffect(
                request.RepositoryId,
                request.OperationId)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task pre_effect_fence_persistence_failure_prevents_repository_effect(
        int failedFlush,
        bool brokerWasAcquired)
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthDirectoryFlush(failedFlush)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        broker.Acquired.ShouldBe(brokerWasAcquired);
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        fixture.LoadOperation(request).EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task uncertain_activity_completion_durability_blocks_later_mutation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var failingDurability = new FailDirectoryFlushFrom(2);

        (await fixture.CreateAuthority(
                new SimulatedBroker(fixture), failingDurability).ApplyAsync(request))
            .IsErr.ShouldBeTrue();
        var visible = fixture.LoadOperation(request);
        visible.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        visible.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        var otherRequest = fixture.Prepare();

        fixture.CreateLedger(failingDurability).HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        fixture.CreateLedger(failingDurability).HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                otherRequest.OperationId)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task a_restarted_apply_does_not_acquire_guards_while_prior_control_activity_is_unresolved()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        (await fixture.CreateAuthority(
                new SimulatedBroker(fixture),
                new FailNthDirectoryFlush(1)).ApplyAsync(request))
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions
            .ShouldHaveSingleItem();
        var replayBroker = new SimulatedBroker(fixture);

        var replay = await fixture.CreateAuthority(replayBroker).ApplyAsync(request);

        replay.IsErr.ShouldBeTrue();
        replay.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        replayBroker.Acquired.ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task no_effect_broker_fails_closed_before_applying()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();

        var result = await fixture.CreateAuthority().ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        restarted.EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task guarded_effect_is_journaled_and_verified_before_applied()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.Applied);
        restarted.EffectJournal.Select(entry => entry.Kind).ShouldBe([
            OperationEffectJournalKind.Intent,
            OperationEffectJournalKind.Admitted,
            OperationEffectJournalKind.Observed
        ]);
        restarted.EffectJournal[0].FenceEpoch.ShouldNotBeNull();
        restarted.EffectJournal[0].FenceEpoch!.Value.ShouldBeGreaterThan(0);
        restarted.EffectJournal[0].FenceToken.ShouldNotBeNull();
        restarted.EffectJournal[0].EffectHash.ShouldBe(
            CanonicalChangePlan.ComputeEffectHash(
                fixture.StoredPlan!.Envelope.Payload.Effects[0]));
        restarted.EffectJournal[1].EffectHash.ShouldBe(restarted.EffectJournal[0].EffectHash);
        restarted.EffectJournal[2].EffectHash.ShouldBe(restarted.EffectJournal[0].EffectHash);
        restarted.EffectJournal[0].GuardTokens.ShouldBe(
            OperationGuardTokenSet.Expected(fixture.StoredPlan.Envelope.Payload));
        restarted.EffectJournal[1].GuardTokens.ShouldBe(restarted.EffectJournal[0].GuardTokens);
        restarted.EffectJournal[2].GuardTokens.ShouldBe(restarted.EffectJournal[0].GuardTokens);
        restarted.OwnerLease.ShouldNotBeNull();
        restarted.OwnerLease!.FenceEpoch.ShouldBe(restarted.EffectJournal[0].FenceEpoch!.Value);
        restarted.OwnerLease.ReservationToken.ShouldBe(restarted.EffectJournal[0].FenceToken!.Value);
        broker.SawDurableIntent.ShouldBeTrue();
        broker.SawDurableAdmission.ShouldBeTrue();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
        broker.ReservationWasHeldThroughVerification.ShouldBeTrue();
    }

    [Theory]
    [InlineData(SimulatedFault.BeforeEffect, false)]
    [InlineData(SimulatedFault.AfterAdmission, false)]
    [InlineData(SimulatedFault.AfterEffect, true)]
    [InlineData(SimulatedFault.BeforeFinalVerification, true)]
    public async Task interrupted_effects_retain_truthful_recovery_state(
        SimulatedFault fault,
        bool targetWasWritten)
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { Fault = fault };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.CurrentState.ShouldNotBe(OperationLifecycleState.Applied);
        restarted.CurrentState.ShouldNotBe(OperationLifecycleState.RolledBack);
        restarted.EffectJournal[0].Kind.ShouldBe(OperationEffectJournalKind.Intent);
        File.Exists(fixture.TargetPath).ShouldBe(targetWasWritten);
        broker.SawDurableIntent.ShouldBeTrue();
    }

    [Fact]
    public async Task broker_cannot_start_when_plan_is_not_pinned_to_the_operation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(pin: false);
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.PlanUnavailable);
        broker.Acquired.ShouldBeFalse();
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
    }

    [Fact]
    public async Task incomplete_guard_evidence_cannot_enter_applying()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { OmitOneGuard = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        fixture.LoadOperation(request).EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task a_broker_that_skips_one_use_admission_cannot_claim_applied()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { SkipAdmission = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal.ShouldHaveSingleItem();
        restarted.EffectJournal[0].Kind.ShouldBe(OperationEffectJournalKind.Intent);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
    }

    [Fact]
    public async Task expired_owner_cannot_admit_a_repository_effect()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { ExpireBeforeAdmission = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal.ShouldHaveSingleItem();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task expired_owner_after_effect_cannot_publish_applied()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { ExpireBeforeFinalVerification = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal[^1].Kind.ShouldBe(OperationEffectJournalKind.Observed);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
    }

    [Fact]
    public async Task an_uncertain_observation_publication_stays_recovery_required()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthDirectoryFlush(7)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState
            .ShouldBe(OperationLifecycleState.RecoveryRequired);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
    }

    [Fact]
    public async Task an_admission_flush_failure_cannot_start_the_effect()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthDirectoryFlush(6)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal[^1].Kind.ShouldBe(OperationEffectJournalKind.Admitted);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task an_admission_replace_failure_cannot_start_the_effect()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthReplace(6)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal[^1].Kind.ShouldBe(OperationEffectJournalKind.Intent);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task an_uncertain_terminal_flush_requires_a_successful_flush_retry()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthDirectoryFlush(9)).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Applied);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
    }

    [Fact]
    public async Task persistent_terminal_flush_failure_blocks_later_mutation()
    {
        using var fixture = new ApplyFixture();
        var firstRequest = fixture.Prepare();
        var durability = new FailDirectoryFlushFrom(9);

        var first = await fixture.CreateAuthority(
            new SimulatedBroker(fixture), durability).ApplyAsync(firstRequest);

        first.IsErr.ShouldBeTrue();
        first.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);

        var secondRequest = fixture.Prepare();
        var secondBroker = new SimulatedBroker(fixture);
        var second = await fixture.CreateAuthority(
            secondBroker, durability).ApplyAsync(secondRequest);

        second.IsErr.ShouldBeTrue();
        second.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        secondBroker.Acquired.ShouldBeFalse();
        fixture.LoadOperation(secondRequest).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
    }

    [Fact]
    public async Task unresolved_mutation_blocks_the_next_operation_before_broker_admission()
    {
        using var fixture = new ApplyFixture();
        var firstRequest = fixture.Prepare();
        var interrupted = new SimulatedBroker(fixture) { Fault = SimulatedFault.AfterEffect };
        (await fixture.CreateAuthority(interrupted).ApplyAsync(firstRequest))
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(firstRequest).CurrentState
            .ShouldBe(OperationLifecycleState.RecoveryRequired);

        var secondRequest = fixture.Prepare();
        var nextBroker = new SimulatedBroker(fixture);
        var second = await fixture.CreateAuthority(nextBroker).ApplyAsync(secondRequest);

        second.IsErr.ShouldBeTrue();
        second.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        nextBroker.Acquired.ShouldBeFalse();
        fixture.LoadOperation(secondRequest).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
    }

    public enum SimulatedFault
    {
        None,
        BeforeEffect,
        AfterAdmission,
        AfterEffect,
        BeforeFinalVerification
    }

    private sealed class SimulatedBroker(ApplyFixture fixture) : IJournaledRepositoryEffectBroker
    {
        internal SimulatedFault Fault { get; init; }
        internal bool OmitOneGuard { get; init; }
        internal bool SkipAdmission { get; init; }
        internal bool ExpireBeforeAdmission { get; init; }
        internal bool ExpireBeforeFinalVerification { get; init; }
        internal bool CancelDuringPreflight { get; init; }
        internal bool Acquired { get; private set; }
        internal bool SawDurableIntent { get; private set; }
        internal bool SawDurableAdmission { get; private set; }
        internal bool ReservationWasHeldThroughVerification { get; private set; }

        public Task<IJournaledRepositoryEffectSession?> AcquireAsync(
            StoredChangePlan plan,
            RepositoryMutationReservation reservation,
            CancellationToken cancellationToken)
        {
            reservation.AssertHeld(plan.Key.RepositoryId);
            Acquired = true;
            return Task.FromResult<IJournaledRepositoryEffectSession?>(
                new Session(this, fixture, plan, reservation));
        }

        private sealed class Session(
            SimulatedBroker broker,
            ApplyFixture fixture,
            StoredChangePlan plan,
            RepositoryMutationReservation reservation) : IJournaledRepositoryEffectSession
        {
            public Task<GuardedPreflightEvidence?> FinalPreflightAsync(
                CancellationToken cancellationToken)
            {
                reservation.AssertHeld("repository-001");
                if (broker.CancelDuringPreflight)
                {
                    fixture.Ledger.CancelBeforeFirstEffect(
                            fixture.Request!.RepositoryId,
                            fixture.Request.OperationId)
                        .IsOk.ShouldBeTrue();
                }
                return Task.FromResult<GuardedPreflightEvidence?>(
                    File.Exists(fixture.TargetPath)
                        ? null
                        : new GuardedPreflightEvidence(
                            broker.OmitOneGuard
                                ? OperationGuardTokenSet.Expected(plan.Envelope.Payload)
                                    .RemoveAt(OperationGuardTokenSet.Expected(plan.Envelope.Payload).Length - 1)
                                : OperationGuardTokenSet.Expected(plan.Envelope.Payload)));
            }

            public Task<string> ApplyEffectAsync(
                ChangePlanEffect effect,
                RepositoryEffectAdmission admission,
                CancellationToken cancellationToken)
            {
                reservation.AssertHeld("repository-001");
                var operation = fixture.LoadOperation(fixture.Request!);
                broker.SawDurableIntent = operation.EffectJournal.Length == 1
                    && operation.EffectJournal[0].Kind == OperationEffectJournalKind.Intent
                    && operation.EffectJournal[0].EvidenceHash == admission.IntentHash;
                if (!broker.SawDurableIntent)
                    throw new IOException("No durable intent preceded the simulated effect.");
                if (broker.Fault == SimulatedFault.BeforeEffect)
                    throw new IOException("Injected interruption before effect.");
                if (broker.ExpireBeforeAdmission)
                    fixture.Now += TimeSpan.FromMinutes(2);
                if (!broker.SkipAdmission)
                {
                    admission.Admit();
                    broker.SawDurableAdmission = fixture.LoadOperation(fixture.Request!)
                        .EffectJournal[^1].Kind == OperationEffectJournalKind.Admitted;
                }
                if (broker.Fault == SimulatedFault.AfterAdmission)
                    throw new IOException("Injected interruption after admission.");
                File.WriteAllBytes(fixture.TargetPath, effect.ReplacementContent.ToArray());
                if (broker.Fault == SimulatedFault.AfterEffect)
                    throw new IOException("Injected interruption after effect.");
                return Task.FromResult(Hash(File.ReadAllBytes(fixture.TargetPath)));
            }

            public Task<string?> VerifyAppliedAsync(CancellationToken cancellationToken)
            {
                reservation.AssertHeld("repository-001");
                broker.ReservationWasHeldThroughVerification = true;
                if (broker.ExpireBeforeFinalVerification)
                    fixture.Now += TimeSpan.FromMinutes(2);
                if (broker.Fault == SimulatedFault.BeforeFinalVerification)
                    throw new IOException("Injected interruption before final verification.");
                return Task.FromResult<string?>(
                    File.ReadAllBytes(fixture.TargetPath).SequenceEqual(fixture.Content)
                        ? Hash(fixture.Content)
                        : null);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ApplyFixture : IDisposable
    {
        private static readonly DateTimeOffset InitialNow =
            new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "rt-journaled-apply-" + Guid.NewGuid().ToString("N"));
        private readonly LocalDurableOperationAuthority _authority;
        private readonly DurableChangePlanStore _store;
        private readonly DurableOperationLedger _ledger;

        internal ApplyFixture()
        {
            RepositoryRoot = Path.Combine(_root, "repository");
            Directory.CreateDirectory(Path.Combine(RepositoryRoot, "src"));
            _authority = new LocalDurableOperationAuthority(Path.Combine(_root, "control"), () => Now);
            _store = new DurableChangePlanStore(
                _authority.PlanStoreRoot,
                ChangePlanStoreOptions.Default,
                () => Now,
                _authority);
            _ledger = new DurableOperationLedger(_authority.LedgerRoot, () => Now, _authority);
        }

        internal string RepositoryRoot { get; }
        internal DateTimeOffset Now { get; set; } = InitialNow;
        internal string TargetPath => Path.Combine(RepositoryRoot, "src", "Added.cs");
        internal byte[] Content { get; } = Encoding.UTF8.GetBytes("class Added;\n");
        internal CanonicalApplyRequest? Request { get; private set; }
        internal StoredChangePlan? StoredPlan { get; private set; }
        internal DurableOperationLedger Ledger => _ledger;
        internal RepositoryMutationReservationProvider Reservations => new(
            _authority.MutationReservationRoot, _authority);
        internal DurableOperationLedger CreateLedger(IOperationLedgerDurability durability) =>
            new(_authority.LedgerRoot, () => Now, _authority, durability);

        internal CanonicalApplyRequest Prepare(bool pin = true)
        {
            var envelope = CreateEnvelope();
            var key = _store.Persist(envelope).Match(value => value, error => throw new IOException(error.Message));
            var operationId = new OperationId(42, Guid.NewGuid());
            var claims = new OperationEpochClaims(
                "repository-001", "principal-001", 42, 8, 1024 * 1024, Now.AddHours(1));
            var request = CanonicalApplyRequest.Create(
                key.RepositoryId,
                operationId,
                key.PlanId,
                key.PlanHash,
                envelope.Payload.Repository.BaseRevision,
                claims);
            Request = request;
            if (pin)
            {
                _store.PinDispatchLease(key, "dispatch-001", Now.AddMinutes(10))
                    .Match(value => value, error => throw new IOException(error.Message));
                _store.AdoptDispatchLease(
                    key,
                    "dispatch-001",
                    DurableDispatchIntakeCoordinator.FormatOperationReference(operationId))
                    .Match(value => value, error => throw new IOException(error.Message));
            }
            StoredPlan = _store.Load(key).Match(value => value, error => throw new IOException(error.Message));
            _ledger.Receive(request, _authority.GetPrincipal())
                .Match(value => value, error => throw new IOException(error.Message));
            _ledger.AppendTransition(
                    request.RepositoryId,
                    request.OperationId,
                    OperationLifecycleState.Received,
                    OperationLifecycleState.Prepared,
                    "Exact plan pinned.")
                .Match(value => value, error => throw new IOException(error.Message));
            return request;
        }

        internal JournaledApplyAuthority CreateAuthority(
            IJournaledRepositoryEffectBroker? broker = null,
            IOperationLedgerDurability? ledgerDurability = null) => new(
            _store,
            ledgerDurability is null
                ? _ledger
                : new DurableOperationLedger(
                    _authority.LedgerRoot, () => Now, _authority, ledgerDurability),
            new RepositoryMutationReservationProvider(_authority.MutationReservationRoot, _authority),
            broker);

        internal DurableOperationRecord LoadOperation(CanonicalApplyRequest request) =>
            new DurableOperationLedger(_authority.LedgerRoot, () => Now, _authority)
                .Load(request.RepositoryId, request.OperationId)
                .Match(value => value, error => throw new IOException(error.Message));

        private ChangePlanEnvelope CreateEnvelope()
        {
            var controlRoot = _authority.ControlRoot;
            var sourcePath = "src/Added.cs";
            var after = new ChangePlanPathState(
                true, ChangePlanPathKind.RegularFile, Hash(Content), null, 1, [],
                "utf-8", false, "lf", "0644", false, false, null);
            var directory = new ChangePlanPathState(
                true, ChangePlanPathKind.Directory, null, "device:test;inode:src", 2, [],
                null, null, null, "0755", true, false, null);
            var payload = new ChangePlanPayload(
                Guid.NewGuid(),
                new ChangePlanRepository(
                    "repository-001", RepositoryRoot, RepositoryRoot,
                    "device:test;inode:repository", new string('a', 40), new string('b', 64)),
                Now.AddMinutes(-1), Now.AddMinutes(20),
                [new ChangePlanOperation(1, "create", sourcePath, [])],
                [new ChangePlanDependency(
                    "src", Path.Combine(RepositoryRoot, "src"),
                    ChangePlanDependencyKind.DirectoryEntry, directory,
                    new ChangePlanGuardRequirement("src", ChangePlanGuardKind.ExternalWriteGuard, "guard-dir"))],
                [new ChangePlanEffect(
                    1, ChangePlanEffectKind.Create, ChangePlanArtifactKind.RoslynDocument,
                    sourcePath, TargetPath, null, null,
                    ChangePlanPathState.Missing, after, null, null, null, Content,
                    [new ChangePlanGuardRequirement(
                        sourcePath, ChangePlanGuardKind.ExternalWriteGuard, "guard-target")])],
                [], [], new ChangePlanConfidence("Proof", "Complete workspace", ["All inputs loaded"]), [],
                new ChangePlanWorkspace(
                    WorkspaceCompletenessState.Complete,
                    Path.Combine(RepositoryRoot, "RoslynTools.slnx"),
                    "Debug", ["net10.0"], ".NET SDK 10.0.100",
                    "Microsoft.CodeAnalysis 5.0.0", "InProcess", "Trusted"),
                [new ChangePlanVerificationAction(1, "build", "dotnet build", "success", true)],
                new ChangePlanCoordination(
                    ChangePlanCoordinationDomain.ExclusivePrincipal,
                    ChangePlanCoordinationMode.ExternalWriteGuards,
                    "one private writer", controlRoot, controlRoot,
                    "device:test;inode:control", "device:test",
                    Path.Combine(controlRoot, "staging"), Path.Combine(controlRoot, "staging"),
                    "device:test;inode:staging",
                    Path.Combine(controlRoot, "backup"), Path.Combine(controlRoot, "backup"),
                    "device:test;inode:backup"),
                new ChangePlanPathPolicy(
                    ChangePlanCaseSensitivity.Sensitive, ChangePlanLongPathPolicy.Reject,
                    ChangePlanReadOnlyPolicy.Reject, ChangePlanSymlinkPolicy.Reject,
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
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class FailNthDirectoryFlush(int failureNumber) : IOperationLedgerDurability
    {
        private int _directoryFlushCount;

        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath) =>
            File.Move(temporaryPath, recordPath, overwrite: true);

        public void FlushDirectoryMetadata(string directory)
        {
            if (Interlocked.Increment(ref _directoryFlushCount) == failureNumber)
                throw new IOException("Injected directory flush failure.");
        }
    }

    private sealed class FailDirectoryFlushFrom(int failureNumber) : IOperationLedgerDurability
    {
        private int _directoryFlushCount;

        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath) =>
            File.Move(temporaryPath, recordPath, overwrite: true);

        public void FlushDirectoryMetadata(string directory)
        {
            if (Interlocked.Increment(ref _directoryFlushCount) >= failureNumber)
                throw new IOException("Injected persistent directory flush failure.");
        }
    }

    private sealed class FailNthReplace(int failureNumber) : IOperationLedgerDurability
    {
        private int _replaceCount;

        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            if (Interlocked.Increment(ref _replaceCount) == failureNumber)
                throw new IOException("Injected record replacement failure.");
            File.Move(temporaryPath, recordPath, overwrite: true);
        }

        public void FlushDirectoryMetadata(string directory)
        {
        }
    }
}
