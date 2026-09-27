# ADR-0027: Internal journaled apply barrier

- Status: Accepted
- Date: 2026-09-27
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

Durable Prepare reaches `Prepared` without repository effects. The existing
`PreparedChangeSetWriter` stages and rolls back in process, but it has no durable
per-effect journal or guard against external writers. Its success and rollback
claims cannot be used for the exact stored-plan apply contract.

## Decision

Add an internal `JournaledApplyAuthority` that loads the exact pinned plan from
protected storage, holds an exclusive repository reservation, checks for unresolved
mutation records, and asks a trusted effect broker to acquire complete filesystem
guards. The authority runs final preflight while those guards are held, appends a
durable intent before each broker effect, records the verified observation, and
commits `Applied` only after a complete observed journal and guarded final
verification. The reservation issues a monotonically increasing durable local fence
epoch under its cross-process lock. Journal events retain that epoch and a
reservation token, and the verified outcome checks every intent against the pinned
plan and held fence. An interruption after `Applying` enters `RecoveryRequired` when that
transition can be persisted. If the final replacement is visible after a directory
flush failure, the authority requires a successful flush retry before reporting
`Applied`; otherwise it reports unavailable durability, and the next operation
must flush that visible terminal record before broker admission. A second operation
cannot enter the broker while another operation for the repository is applying or
unresolved.

The broker is a host capability, not an ordinary filesystem adapter. Its contract
requires OS-enforced external-write exclusion or atomic conditional effects over
all reviewed dependencies, one-use admission bound to the held reservation and
durable intent, and final verification before guard release. No production broker
currently satisfies that contract, so the default broker fails closed before
`Applying`. A simulated broker is used only for internal fault tests. The local
host can provision a private reservation root, but a reservation serializes
participating Roslyn Tools operations with the same repository ID only. It does
not itself prove physical repository identity or guard against external writers.

This internal boundary remains unavailable for production effects. The intent journal binds the
exact effect payload, the local fence epoch, and the acquired guard/version token
set. A durable owner lease must remain unexpired at intent, admission, and
terminal publication; an expired owner cannot renew itself. A late observation
can still document an already admitted effect without claiming `Applied`. The
protected ledger records one-use effect admission before accepting an observation. The trusted
broker contract still has no production implementation that can prove OS-enforced
guard provenance, atomic conditional effect admission, complete intermediate
repository state, or guarded final filesystem verification. Staging/backup intent
and a pre-effect activity fence also remain missing. These are prerequisites for
enabling a production filesystem effect or accepting this boundary as a complete
apply authority.

The generic ledger transition method rejects unproven outcomes from effect states.
The dedicated verified-applied transition requires a complete observed journal.
There is no verified rollback or recovery transition in this slice.

## Consequences

- The internal authority cannot issue a repository effect in the production host
  until a reviewed physical broker exists. Public Apply remains unavailable.
- Simulated interruptions before an effect, after an effect, during observation
  persistence, and during final outcome persistence retain a truthful durable state.
- `RecoveryRequired` blocks the next operation for the repository. Guarded
  reconciliation, rollback, artifact cleanup, and public lifecycle/status are
  follow-up work under issue #123.
- The earlier in-process writer remains separate from this durable apply path.

## Alternatives considered

- Reuse `PreparedChangeSetWriter`: rejected because its check-then-write and
  process-local rollback cannot satisfy the durable effect and external-writer
  contract.
- Treat the repository reservation as an external-writer guard: rejected because
  editors and scripts do not participate in that reservation.
