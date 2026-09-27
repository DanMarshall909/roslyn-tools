# ADR-0036: Atomically hand a guarded preflight to the first effect intent

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

Final preflight holds a repository guard while a pre-effect activity admission
blocks no-effect cleanup. The earlier apply path drained that activity before
releasing a failed or cancelled guard. It also drained the activity, claimed an
apply owner, and appended the first intent in separate ledger writes. Cancellation
could win between those writes while the guard remained held.

## Decision

On a failed preflight or a cancellation that wins the handoff, release the guard
session before draining its pre-effect activity. A failed release leaves the
admission active, so no-effect cleanup cannot be confirmed. The caller returns
an unsafe-coordination error with the release failure.

After a successful guarded preflight, one ledger replacement validates the exact
pinned plan and guard evidence, closes and advances the pre-effect fence, drains
the final activity, claims the apply owner, records `Applying`, and appends the
first effect `Intent`. Cancellation uses the same ledger lock. If it has already
won, the handoff leaves the admission active for guard release and subsequent
drain. An uncertain durability flush may leave the complete handoff visible;
the caller performs no repository effect and leaves the intent for conservative
reconciliation. The internal apply authority uses this handoff; legacy ledger
transition helpers remain for existing tests and require a separate migration
before the ledger API itself can enforce this rule everywhere.

## Limits and follow-up

This is an internal ledger and simulated-broker boundary. It does not prove a
physical repository guard or authorize public Apply. The first intent alone is
not an effect observation or proof of a repository write. Physical writer
exclusion, effect-time validation, rollback, recovery, and public lifecycle
operations remain open under #138, #140, and #123.
