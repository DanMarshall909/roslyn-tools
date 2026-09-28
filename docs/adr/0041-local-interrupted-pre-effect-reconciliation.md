# ADR-0041: Reconcile interrupted pre-effect work under a later local reservation

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

A cancelled operation could retain an active pre-effect admission after its owner
stopped. A new local reservation could finish cleanup only after the original
owner drained that admission. A crash before guard acquisition, after confirmed
guard release, or between owner claim and activity admission therefore stranded
an otherwise provable no-effect operation and its plan pin.

## Decision

The internal no-effect cleanup service may reconcile an interrupted owner while
holding a later exclusive reservation issued by the same protected local
authority. The ledger checks one earlier source reservation across the active
admissions, guard history, owner lease, and admitted staging and backup artifacts.
Every active admission must have an `ActivityAdmitted` record with no later guard
attempt, or a `ConfirmedInactive` record. An owner claim with no admission may
also be reconciled if the durable record has no admission history or artifact
journal. A missing guard record, unresolved `AcquisitionIntent`, mixed source, or
any first-effect intent blocks reconciliation.

Staging and backup writers hold an activity scope on their OS reservation
through the physical control write. Cleanup holds the same scope through removal
and pin handling. Disposing a reservation revokes new work, but its OS lock
remains held until all such scopes leave. A successor therefore cannot treat an
in-flight local control writer as an interrupted owner because another thread
disposed the old reservation. Process termination closes the lock and the
in-process writers together.

One durable ledger replacement closes the admission fence, drains the proven
activities, records `RejectedUnsafeCoordination` when the prior state was
`Prepared` or `Applying`, and inventories admitted artifacts. Existing terminal
no-effect outcomes keep their state and inventory. An empty inventory is recorded
for an interrupted owner without artifacts so cleanup can still verify both
operation roots before releasing the plan pin. For every cleanup, including a
partial artifact journal, the exact retained plan must remain readable and
bind both control roots; the backup boundary is derived from that plan when
the caller does not supply one. A still-unverified cleanup requires the durable
operation pin. Missing plan evidence or either mismatched root retains the pin.
The cleanup service
flushes and reloads the terminal, drained record before any physical removal.
Cleanup then uses the existing handle-relative boundaries, journaled removal,
and empty-root verification. A failed ledger write, cleanup boundary, or pin release leaves
the operation retryable; a possibly active guard returns a distinct unresolved
error and retains the pin. After successful plan-store pin release, the ledger
durably marks that release as confirmed. A missing exact plan is unresolved
until this marker exists: `CleanupVerified` alone precedes release and cannot
distinguish completed collection from premature plan loss. Once the marker is
durable, replay returns the already verified terminal record without new
physical work, including after plan collection. If release succeeds but marker
recording fails, a retained plan permits a rescan and idempotent release retry;
if the plan disappears first, cleanup fails closed pending recovery.

## Consequences and follow-up

Restart can complete a no-effect local operation after its prior owner loses the
reservation, including a creation admission whose file was never created. A
stale owner cannot use its old admission under the successor reservation. Mixed
reservation provenance, uncertain guard release, and mismatched physical
artifacts remain pinned for explicit recovery. This does not authorize a
repository effect or establish a shared authority. #140 still needs the wider
process interruption matrix; #138 must prove the production effect broker and
#139 owns shared reservation and epoch continuity.

## Alternative considered

Process disappearance alone is not proof that a guard became inactive. Releasing
the pin or removing artifacts from that inference could race a still active
broker session, so the reconciler requires durable guard history instead.
