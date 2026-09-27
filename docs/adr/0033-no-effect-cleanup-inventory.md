# ADR-0033: Capture no-effect cleanup inventory with the terminal transition

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

Cancellation may close new pre-effect admission while an earlier staging
creation is still active. An artifact can therefore appear or finish
verification after the no-effect transition. Cleanup needs a durable list of
every path whose creation was admitted before closure, including work that has
not drained yet. Deriving that list only after activity drains would lose the
atomic cancellation boundary and leave a restart with no explicit cleanup
state.

## Decision

The protected operation ledger appends an `InventoryPending` cleanup event in
the same record replacement as each terminal no-effect transition that has
staging history. Its ordered inventory copies the effect sequence, path,
expected hash, creation token, and reservation fence from every durable
`CreationAdmitted` event. A staging intent without creation admission produces
an empty pending inventory; the eventual control-root rescan still has to prove
cleanup. No later creation admission can add a new path after the fence closes.
An already admitted action may finish verification without changing this
inventory.

The restart reader validates the inventory against the immutable staging
journal and rejects a missing or changed cleanup event for a terminal
no-effect operation with staging history. The plan and journal remain pinned
while cleanup is pending.

## Limits and follow-up

This event names cleanup work; it does not remove an artifact or claim
`CleanupVerified`. Physical cleanup must wait for all admitted activity to
drain, journal each durable removal, and prove the protected operation root
empty through an exclusive handle-relative rescan before releasing pins.
Cross-owner restart cleanup additionally needs the shared authority and stale
writer quiescence proof. Public Apply and repository effects remain disabled.
ADR-0034 subsequently adds a bounded same-reservation staging cleanup path.
