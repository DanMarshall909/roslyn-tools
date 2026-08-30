# ADR-0013: Fenced plan-unavailability resolution

- Status: Accepted
- Date: 2026-08-30
- Extends: the caller-known mutation identity and status protocol in [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

After mutation dispatch becomes indeterminate, status must fence a delayed receiver
before the caller can safely decide what to do next. The existing `NotRecorded` path
required the reviewed plan to remain accessible so it could pin that plan and issue a
one-use replay authorization. Normal plan expiry, storage failure, or corruption
between dispatch and status could make that prerequisite impossible, leaving the same
operation permanently indeterminate.

A missing plan does not itself prove that no receiver is still using previously read
bytes. Absence therefore cannot authorize a new operation until the original identity
is durably fenced under the intake authority.

## Decision

Before any mutation request bytes are sent, the caller registers a quota-bounded
`DispatchPlanLease` under the caller-known repository/operation identity. The protected
authority validates the authenticated operation epoch, canonical request identity, and
exact persisted plan envelope/hash, then pins those bytes through durable receipt
adoption, authoritative status resolution, or epoch sealing and reserves capacity for
the truthful `NotRecorded` or terminal dispatch-resolution record. The receiver
requires the matching lease and atomically adopts its pin when recording `Received`.
The lease is not evidence that transport accepted the request.

Receipt adoption transfers the pin into the operation lifecycle. Operation-level
`Expired` and terminal no-effect outcomes with a valid plan retain its exact bytes,
journal, artifact inventory, quota reservation, and cleanup capability while cleanup
is pending. `RejectedPlanUnavailable` retains the immutable identity/hash, any
remaining verified bytes, failure evidence, and the rest of the cleanup bundle without
reconstruction. Epoch sealing or durable lease/replay expiry releases only an unadopted pin
for which no operation or control artifact exists; an adopted pin releases only after
verified terminal cleanup or explicit recovery acknowledgement.

When indeterminate status finds no receipt or operation, it validates the original
token and request identity, inspects the dispatch lease under the same intake fence,
and excludes every live intake attempt. If the exact plan is readable and verified,
status records `NotRecorded` and may return the existing one-use replay authorization. If the lease
or plan is missing, expired, unreadable, schema-incompatible, or hash-mismatched,
status instead records terminal no-effect `RejectedPlanUnavailable` with bounded reason
and evidence and returns no replay authorization.

Both outcomes permanently fence a delayed original receiver for that identity. A
later reappearance of plan bytes cannot revive `RejectedPlanUnavailable`. Only after
observing that terminal outcome may a caller re-prepare current inputs and use a new
current-epoch operation ID. If `Received` already exists and its plan becomes
unavailable before `FirstEffectStarted`, the accepted operation records the same
terminal no-effect outcome rather than remaining nonterminal or reconstructing work.

Lease count and pinned bytes are bounded per repository and principal. Failure to
reserve a dispatch lease occurs before request bytes are sent. Transfer, retirement,
and epoch sealing fence new leases and quiesce or expire existing leases before
changing authority or releasing their pins.

Proven `NotDispatched` may release a lease early only under the intake fence after the
authority proves that no intake attempt or receipt exists. A caller crash before
dispatch leaves a quota-counted lease until epoch sealing rather than an unsafe
absence inference.

## Consequences

- Ordinary retention cannot strand an indeterminate operation by expiring its plan.
- Operation expiry cannot strand staged-artifact cleanup by releasing an adopted plan
  pin before the complete cleanup bundle is verified.
- Storage loss or corruption has an explicit non-replayable status outcome.
- A new plan and operation identity are allowed only after the old receiver is durably
  fenced.
- Implementations need lease quota, crash, garbage-collection, transfer, retirement,
  corruption, and delayed-receiver fault-injection tests.

## Alternatives considered

- Let `NotRecorded` authorize replay without plan validation: rejected because replay
  could execute missing or different bytes.
- Infer no dispatch from a missing plan: rejected because a delayed receiver may
  already hold the original bytes.
- Remain indeterminate until epoch sealing: rejected because avoidable plan-store
  failures would block status for the entire epoch and encourage unsafe new attempts.

## References

- Related ADRs: [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- Related docs: [architecture overview](../architecture-overview.md), [critical platform hardening design](../../openspec/changes/critical-platform-hardening/design.md)
