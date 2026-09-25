# ADR-0025: Restart-safe dispatch and intake handoff

- Status: Accepted
- Date: 2026-09-25
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

[ADR-0023](0023-protected-durable-change-plan-storage.md) defines exact plan
storage and plan pins. [ADR-0024](0024-durable-operation-intake-ledger.md)
defines the durable `Received` receipt and lifecycle ledger. Their atomic files
and locks are deliberately independent. Calling them in either order does not
create a safe handoff:

- receipt first can leave accepted work backed only by an expiring dispatch pin;
- pin adoption first can leave a non-expiring pin without evidence that intake
  accepted the request; and
- two different requests using one operation ID can otherwise race through two
  different plan records.

A filesystem lock spanning the calls would serialize processes but would not make
two atomic replacements crash-atomic. The handoff therefore needs durable intent
and idempotent reconciliation rather than an in-memory critical section.

## Decision

`DurableDispatchIntakeCoordinator` owns a no-effect handoff from an exact stored
plan to a durable `Prepared` operation. Before dispatch, it writes an
operation-addressed dispatch record under the protected ledger authority. That
record binds the repository and structured operation ID to the canonical request
bytes and identity, authenticated principal, exact plan key, dispatch-plan lease,
intake-attempt identity, both expiries, current protocol phase, and a sequenced
phase history.

The dispatch record is coordination metadata, not a public operation lifecycle
state. In particular, `IntakePinned` is not exposed as a canonical `Pending`
operation. Until the durable receipt exists, public transport knowledge remains
indeterminate. The record provides the intake fence: an exact request by the
retained principal resumes the same protocol, different work conflicts before its
plan can be pinned, and another principal receives no operation details.

The plan pin moves through three explicit kinds:

```text
DispatchLease -> IntakePending -> OperationLedger
```

`IntakePending` is non-expiring. It closes the unsafe gap in which a valid receipt
could outlive the plan bytes. Before the first dispatch pin, the coordinator verifies
the stored plan's repository, ID, hash, and base revision against the canonical
request. Once the intake pin is durable, the coordinator records `Received`, promotes
the pin to the operation ledger, and appends `Prepared`. `Prepared` authorizes no
repository write; later effect admission still requires the repository reservation,
fencing epoch, effect fence, and durable journal.

Each leaf replacement is independently atomic. Pin mutations are exact-replay
idempotent; ledger and phase transitions use compare-and-set and become replay-safe
through coordinator read-back and reconciliation. The durable phase record makes
each implemented inter-store frontier restart-reconcilable. After a late durability
error, the coordinator reads back the exact pin, receipt, or phase and continues only
when the expected committed state is proven; otherwise it returns a coordination
error without guessing. A fresh coordinator can then reload the phase and retry
without duplicating the receipt, pin promotion, or lifecycle transition.
Authoritatively missing, unretained-expired, incompatible, hash-mismatched, or
revision-mismatched plans are fenced as `RejectedPlanUnavailable`; if intake was
already accepted, the lifecycle retains `Received -> RejectedPlanUnavailable` and
no repository effect occurs. An unreadable or corrupt plan during uncertain-write
reconciliation remains indeterminate so the coordinator does not guess whether a
replacement committed. An exact live dispatch pin remains authoritative when the
plan's ordinary payload lifetime expires before phase reconciliation.

The coordinator remains internal and uses injected protection authorities. The
default plan and ledger authorities still fail closed, so this decision does not
publish an apply or status surface.

## Consequences

- Exact replay after `Prepared` reads retained state without plan reload, pin churn,
  a new receipt, or another lifecycle transition.
- Concurrent identical intake converges on one receipt, one operation pin, and one
  `Prepared` transition.
- A crash after any dispatch-record, pin, receipt, or phase update has a durable
  frontier from which a later process can resume.
- The operation ledger never records `Received` until a non-expiring intake pin
  protects the exact plan, except the explicit terminal plan-unavailable path whose
  dispatch record retains the failed reference and evidence.
- Pending and operation pins survive ordinary plan expiry and garbage collection.
- Production protection, bounded intake-attempt expiry resolution, `NotRecorded`,
  lifecycle authorization, repository reservation/barriers, effects, recovery, and
  public status remain required.

## Alternatives considered

- **Hold the plan-store and ledger locks at once.** Rejected because lock nesting
  cannot make two file replacements crash-atomic and would introduce lock-order risk.
- **Expose `Pending` as a lifecycle state.** Rejected because the accepted lifecycle
  begins at durable `Received`; a live intake attempt is internal transport and
  coordination evidence.
- **Release an orphaned intake pin after timeout.** Rejected until authoritative
  `NotRecorded` resolution proves no delayed receiver can commit the receipt.
- **Call the existing prepared filesystem writer after `Prepared`.** Rejected because
  it has no restart-safe journal, repository reservation, effect fence, or verified
  rollback.

## References

- Related ADRs: [ADR-0013](0013-fenced-plan-unavailability-resolution.md),
  [ADR-0023](0023-protected-durable-change-plan-storage.md),
  [ADR-0024](0024-durable-operation-intake-ledger.md)
- Related docs: [architecture overview](../architecture-overview.md)
- Specification: [apply operations have caller-known durable identity](../../openspec/changes/critical-platform-hardening/specs/platform-hardening/spec.md#requirement-apply-operations-have-caller-known-durable-identity)
