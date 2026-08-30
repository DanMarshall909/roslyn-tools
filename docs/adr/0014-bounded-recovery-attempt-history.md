# ADR-0014: Bounded recovery-attempt history

- Status: Accepted
- Date: 2026-08-31
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)
- Related ADR: [ADR-0008](0008-daemon-transparent-passthrough-phase.md)

## Context

An operation in `RecoveryRequired` may need several authorized resume or rollback
attempts before its repository state can be proven. Keeping every attempt transition,
journal segment, observation, and diagnostic forever makes one pinned operation an
unbounded coordination-store record. A quota on the number of pinned operations does
not bound that internal growth, and rejecting a later transition for quota would make
recovery unable to report the truth.

Recovery still needs exact current evidence: the retained plan and base journal,
backups, unresolved effect intents, the last verified per-path state, and the remaining
resume or rollback actions. Older completed attempt detail is audit history, not the
actionable checkpoint for the next attempt.

## Decision

Admission for every accepted mutation reserves bounded capacity for a fixed-size
`RecoveryAttemptAccumulator`, monotonic `RecoveryAttemptSequence` and
`CompactedThroughRecoveryAttempt`, a plan-bounded `CurrentRecoveryCheckpoint`, a
bounded hot attempt window, one maximum-size in-flight attempt journal segment, one
plan-bounded active recovery-artifact set, and the next truthful state transition.
Arbitrary diagnostic text and tool output are capped; their complete bounded digest is
retained when detail is omitted.

Only one recovery attempt can be active for a repository operation. Before a new
attempt may perform an effect, any prior fully quiesced attempt outside the hot window
is folded atomically into the authenticated accumulator, the compaction watermark is
advanced, and its reusable slot is released. A prior attempt cannot be compacted while
its owner, effect fence, effect intent, observed result, or checkpoint is unresolved.
If compaction or continuity cannot be proven, the operation remains
`RecoveryRequired` and the new attempt returns `RecoveryAttemptUnavailable` before any
repository effect.

An inconclusive attempt uses its reserved capacity to commit a bounded immutable
summary and atomically replace `CurrentRecoveryCheckpoint` with the exact latest
verified journal frontier, per-affected-path existence/type/identity/hash/metadata,
unresolved intents, retained plan/backup references, remaining deterministic actions,
active recovery-artifact inventory, and outcome classification. It then returns to
`RecoveryRequired`. Superseded attempt artifacts and older detail may be deleted or
moved to a separately quota-bound archive only after the accumulator, replacement
checkpoint, and active-artifact-set commit. Successful recovery retains its terminal
evidence under the normal bounded outcome policy.

The recovery-attempt sequence, accumulator, watermark, and checkpoint are protected by
the coordination authority's continuity mechanism. Startup or restore must prove their
agreement; otherwise the recovery barrier remains active and no recovery effect is
admitted. The ledger is logically append-only: sequences are never reused and compacted
history cannot be rewritten, reordered, or made to disappear without changing the
authenticated accumulator.

## Consequences

- Repeated inconclusive recovery attempts consume bounded coordination-store space.
- Every admitted attempt can record a truthful outcome using capacity reserved before
  the original mutation was accepted.
- The next attempt retains exact actionable evidence while old detailed diagnostics
  may leave the hot store after authenticated compaction.
- Recovery may pause before effects when an earlier attempt is not quiescent or the
  authority cannot prove compaction continuity; it never overwrites ambiguous evidence.
- Tests must cover attempts beyond the hot window, diagnostic floods, compaction and
  checkpoint crash boundaries, stale-owner reconciliation, rollback detection, and
  eventual successful recovery.

## Alternatives considered

- Retain every attempt forever: rejected because one pinned operation remains an
  unbounded storage-exhaustion path.
- Cap the number of recovery attempts: rejected because it can permanently strand a
  recoverable repository.
- Drop old evidence without an accumulator and current checkpoint: rejected because it
  permits undetectable history loss and can remove facts needed for the next recovery.
- Store only hashes: rejected for the current checkpoint because recovery needs exact
  current per-path and journal-frontier evidence, not merely an audit commitment.

## References

- Related docs: [architecture overview](../architecture-overview.md), [critical platform hardening design](../../openspec/changes/critical-platform-hardening/design.md)
