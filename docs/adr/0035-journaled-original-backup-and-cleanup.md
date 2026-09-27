# ADR-0035: Journal exact original backups before repository effects

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

The immutable plan already contains the exact original bytes for replacement,
deletion, and move effects and names a separate protected backup root. The
internal staging path and no-effect cleanup could previously handle only
replacement artifacts. A repository effect cannot safely depend on a backup
that has no durable creation admission, physical verification, or cleanup
inventory.

## Decision

For an original-content effect, the internal ledger records backup `Intent`,
`CreationAdmitted`, and `Verified` events under a held repository reservation
and pre-effect activity. The intent binds the plan's original-byte hash, effect
sequence, operation-scoped `.backup` path, control generation, and reservation
fence. Creation admission precedes any file creation. The Linux x64 backup
boundary opens the configured private root through directory handles, proves
it shares the repository mount without overlapping the repository root,
creates the admitted path, flushes the file and containing directory, and
hashes its physical bytes before verification is journaled.

An interrupted creation can be retried only with the original activity and
held reservation. A verified replay reopens and hashes the existing file; it
does not recreate a missing artifact. No-effect cancellation captures every
admitted staging and backup artifact in one immutable cleanup inventory before
closing admission. After all activity drains, cleanup checks each kind against
its configured root and removes exact artifacts through directory handles.
It then proves both operation directories empty, appends `CleanupVerified`,
confirms durable ledger state, and releases the plan pin. Changed, redirected,
linked, unknown, or wrong-reservation artifacts leave cleanup pending.

## Limits and follow-up

The backup bytes come from the reviewed immutable plan. The repository's live
original file and external-write guard must be revalidated at effect time;
this path does not claim that proof. It is an internal pre-effect primitive,
not repository write authority or public Apply. Guard cleanup, stale-writer
quiescence, shared-principal takeover, effect/rollback/recovery, and public
lifecycle routing remain open under #140 and #123. Other platforms fail
closed.
