# ADR-0024: Durable operation intake and lifecycle ledger

- Status: Accepted
- Date: 2026-09-25
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

[ADR-0023](0023-protected-durable-change-plan-storage.md) can retain the exact plan
bytes for an operation, and the canonical apply request supplies a stable request
hash and caller-known operation ID. Neither primitive answers whether a process
accepted a request, whether the same identity already names other work, or which
lifecycle transitions survived a restart. Process-local dictionaries or a terminal
result alone cannot provide at-most-once intake.

Intake is also an authorization boundary. Repository or request fields cannot assert
the submitting principal, and another principal who learns an operation ID must not
receive an existence-versus-conflict oracle.

## Decision

`DurableOperationLedger` stores one versioned atomic operation snapshot per canonical
repository ID plus structured operation ID. The snapshot combines the durable intake
receipt, exact canonical request bytes and identity, submitting principal, current
lifecycle state, and a monotonically sequenced transition history. Persisting the
initial `Received` transition and receipt in one snapshot removes a physical
receipt-to-ledger gap while retaining both semantic roles. Later providers may use
separate records only if their promotion is equally atomic or restart-reconcilable.

Repository text is never a path component. The repository ID is SHA-256 digested;
the authenticated epoch and nonce form the remaining address. Every read validates
that the record matches that address, recomputes the canonical request hash, and
checks a contiguous, time-ordered, legal transition history whose final entry agrees
with the current state. Corrupt records are retained and rejected rather than
replaced by a replay.

Intake receives an `AuthenticatedPrincipalId`, not a request string with implied
authority. The protected host or transport boundary is responsible for constructing
that value. An exact request by the retained principal returns the existing record
without a write. Different work under the same retained identity returns a
response-only `Conflict` and does not append or terminate the canonical operation.
Another principal receives one `AccessDenied` disposition for both exact and
conflicting attempts, with no operation data.

Every operation requires a trusted host-issued physical-protection lease before the
engine creates or reads its root. The default authority refuses access without
touching the proposed path. A transition is published as a complete same-directory
temporary record: content and file metadata are flushed, the record is atomically
replaced, then directory metadata is flushed. The ordered durability boundary is
injectable. A failure before replacement leaves the prior snapshot authoritative; a
failure after replacement is indeterminate to the caller but discoverable by a fresh
status read. A cross-process exclusive ledger lock serializes intake and transitions.

This is an internal persistence foundation, not an apply authority. A production
protection authority, repository enrollment and operation-epoch issuance, dispatch
plan leases and atomic pin adoption, complete lifecycle authorization, quotas and
retention, intake-attempt fencing, journal/effect ownership, recovery, CLI status,
and shared-authority storage remain required before the ledger participates in public
mutation dispatch.

## Consequences

- Restarted processes can distinguish a durable receipt from absence and can inspect
  one logically append-only lifecycle history.
- Concurrent identical intake converges on one `Accepted` result and retained replays;
  conflicting reuse never mutates the selected operation.
- Authority-bearing principal identity is separate from request data, and denied
  callers receive no operation record.
- Corruption and late durability failure remain queryable evidence instead of causing
  silent replay or record reconstruction.
- The store deliberately fails closed until a production physical-protection
  authority is wired.
- This decision does not complete OpenSpec P0.2: it supplies the receipt/ledger engine
  on which dispatch fencing, authorization, bounded retention, and recovery still
  depend.

## Alternatives considered

- **Write a receipt and ledger record separately.** Rejected for the first engine
  because it creates a promotion crash boundary without adding useful semantics.
- **Treat `OperationId` as a bearer capability.** Rejected because identities are
  addresses; every lifecycle action needs current authorization.
- **Overwrite a corrupt record from a matching replay.** Rejected because corruption
  may be the only evidence that the operation was accepted.
- **Return conflict details to any caller.** Rejected because it exposes operation
  existence and request equality across principals.

## References

- Related ADRs: [ADR-0013](0013-fenced-plan-unavailability-resolution.md),
  [ADR-0015](0015-operation-lifecycle-authorization.md),
  [ADR-0023](0023-protected-durable-change-plan-storage.md)
- Related docs: [architecture overview](../architecture-overview.md)
- Specification: [apply operations have caller-known durable identity](../../openspec/changes/critical-platform-hardening/specs/platform-hardening/spec.md#requirement-apply-operations-have-caller-known-durable-identity)
