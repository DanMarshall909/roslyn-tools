# ADR-0034: Journal same-reservation no-effect staging cleanup

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

ADR-0033 records every admitted staging creation in the no-effect terminal
record. The artifacts remain outside the checkout and the plan remains pinned
until their removal and the empty operation root are durably proved. A crash
may leave a file or directory removal visible without its following journal
event, so cleanup must retry from the retained inventory.

## Decision

An internal Linux x64 cleanup runner accepts only a terminal no-effect
operation with no effect journal, a closed and drained pre-effect activity
fence, and the same held reservation recorded by the last staging activity
and every admitted artifact in the cleanup inventory. An operation that staged
artifacts under multiple reservations remains pending until a later shared
authority protocol can prove safe takeover.
It checks that every staging event names its configured protected root. A new
reservation cannot inherit pending physical cleanup; cross-owner handoff needs
shared authority and stale-writer quiescence proof.

The `InventoryPending` event is the durable deletion intent. For each artifact
in inventory order, cleanup opens private directories and a private, singly
linked regular file through directory handles, checks its reviewed hash and
identity, unlinks it relative to the operation directory, and flushes that
directory. A missing artifact is accepted only after flushing its parent to
confirm the observed absence. The ledger then appends `ArtifactRemoved` for
that exact inventory entry. If a flush or append fails, retry reconciles the
same path from the inventory. An unknown or changed entry is left in place.

After all admitted artifacts are recorded removed, handle-relative
`unlinkat(AT_REMOVEDIR)` atomically requires the operation directory to be
empty and removes it; cleanup flushes the epoch directory before appending
`CleanupVerified`. An unexpected entry makes the empty-directory removal fail
closed. When a directory in the configured root path is already absent,
cleanup flushes its opened parent before accepting that absence; an uncertain
flush keeps cleanup and the plan pin pending. An intent-only operation can
verify an absent operation root without creating one. The ledger reader
validates ordered removal and verification events against the terminal
inventory.

Before releasing the operation's plan pin, cleanup confirms the verified
ledger record's directory durability. An uncertain `CleanupVerified` write
therefore retains the pin. Replaying verified cleanup after normal collection
of an already unpinned plan succeeds without recreating the plan.

## Limits and follow-up

This is an internal staging-only path. It does not route startup or public
status through cleanup, prove guard release, clean backups, or reconcile an
unreachable active admission. The current exclusive-principal boundary and
same-reservation check do not establish a shared cross-principal writer set or
safe takeover. Other platforms fail closed. Public repository effects and
Apply remain disabled. Those broader #140 and #123 obligations remain open.
