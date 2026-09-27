# ADR-0029: Internal staging intent journal

- Status: Accepted
- Date: 2026-09-27
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

ADR-0028 persists pre-effect admissions but does not identify a planned staging
artifact. A future control-root worker needs a durable, exact-plan intent before
it can create one, and restart or cancellation needs to distinguish a pending
artifact from a verified one.

## Decision

The protected operation record now has a bounded, append-only staging journal.
An open pre-effect admission and held repository reservation can append an intent
for replacement bytes from the exact verified plan. The ledger derives a stable
operation-scoped path below the plan's staging root and records its expected hash,
effect sequence, control generation, admission token, and reservation fence. A
broker-reported hash can append a verification event only while that same
admission and generation remain current. The ledger rechecks uncertain directory
durability before accepting an exact replay. Restart loading validates event
order, path shape, hashes, and fence identity. The first repository effect is
blocked while any staging intent lacks verification.

Cancellation closes new journal events while retaining an unfinished intent as
cleanup evidence. No staging file is created by this ledger slice.

## Limits and follow-up

The production host has no control-root artifact broker, physical-root
protection proof, or staged writer. A verification event is a trusted broker
observation contract, not a filesystem proof by the ledger itself. No public
Apply is enabled. #140 still needs per-action non-bypassable control authority,
durable artifact creation and inspection, backup inventory, guard release,
cleanup and restart reconciliation. The provisional event bound must be
reserved at intake before production staging can rely on it.
