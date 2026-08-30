# Critical platform hardening

- Status: proposed
- Priority: P0/P1
- Decision owner: architecture
- Supersedes for this phase: daemon workspace-caching work
- Reviewed implementation baseline: `main` at `cca0c55ea93ea26281fc4ff6f0f91750464bdb29`

## Why

The current tool is valuable, but its advertised execution safety, protocol maturity, distribution status, and cross-host parity exceed the guarantees enforced by the runtime.

The most serious gaps are:

- repository-controlled MSBuild evaluation is performed without an explicit trusted-workspace boundary or isolated evaluator;
- legacy daemon routing can still fall back in-process after a mutation may have been dispatched;
- the staged writer provides in-process compensating rollback for supported changes, but plans, journals, status, and failed-rollback evidence do not survive restart and path containment is not protected atomically against symlink/reparse races;
- incomplete workspace loads can produce proof-like findings and deletion advice;
- the handwritten MCP host does not conform to the lifecycle and message rules of the versions it advertises, and does not expose real structured results;
- Debug builds mutate the working tree and user profile, while release metadata advertises `1.0.0` despite no corresponding release.

The typed RPC recovery coordinator, `PreparedChangeSet`, staged compensating writer,
host-neutral follow-on metadata, and command-migration ratchet now on `main` are
useful foundations. The writer preflights and stages supported changes, backs up
originals, suppresses cancellation during commit, and attempts reverse-order
rollback. It remains a process-local foundation rather than completion of the
durable safety kernel.

## Scope

This change establishes:

- an explicit trusted-workspace policy and an isolated execution boundary for repository-controlled MSBuild, analyzers, and generators;
- a prepared-change-set mutation safety kernel with durable operation identity, idempotency, status lookup, rollback, and recovery;
- a `complete | partial | failed` workspace contract with provenance and shared confidence policy;
- a canonical typed execution spine for `outline`, `find-refs`, `find-unused`, `rename`, and `safe-delete`;
- a conformant, versioned MCP surface with structured content, bounded resources, and executable schemas;
- catalogue-derived metadata, examples, and command admission rules;
- hermetic build, dependency, CI, installer, and release gates;
- a temporary daemon constrained to transparent transport passthrough.

## Non-goals

- Implementing a daemon-owned workspace cache, session manager, incremental invalidation, or multi-repository service.
- Broadly converting all commands before the representative vertical spine is complete.
- Replacing discoverable typed tools with a generic untyped execution endpoint.
- Claiming that a multi-file filesystem transaction can be perfectly atomic on every platform. The required guarantee is deterministic preflight, journalling, rollback where possible, and an explicit `RecoveryRequired` state where it is not.
- Automatically trusting a checkout because it is local or because the user opened it in an editor.

## Decision

The daemon is not an execution authority in this change. It is an optional transport adapter that forwards a typed canonical request and returns the canonical result without semantic change. It must not own workspace state or mutations, retry after indeterminate mutation dispatch, manufacture fallback execution, use process-global request context, log raw payloads, or change diagnostics, result shape, cancellation, provenance, or safety policy.

Every apply-capable request is assigned a caller-generated operation ID before dispatch. That ID is the durable idempotency and status-lookup handle. A separate request-attempt ID may be used for transport correlation, but a server-generated ID that can be lost with the response is insufficient.

Prepare atomically persists the exact canonical plan in a user-private store outside
the target repository and returns its repository ID, plan ID, and plan hash. Apply
loads those exact bytes across process restart; it never recomputes the reviewed work
from command arguments or a newer workspace snapshot.

A failed or interrupted rollback is never reported as clean rollback. The operation enters `RecoveryRequired`; its journal, backups, diagnostics, and deterministic recovery guidance are retained until recovery or explicit acknowledgement.

Every `Applying` record has a renewable owner lease. Expired owners transition
through bounded `Reconciling` work on startup or status lookup, and finish only as a
verified apply, verified rollback, or `RecoveryRequired`. Filesystem effects use
handle-relative no-follow/reparse-safe operations, or unattended apply is blocked
when the platform cannot prove containment at the side-effect boundary.

Untrusted repositories are not evaluated inside the long-lived CLI, MCP, or daemon host. The tool must refuse, obtain explicit trust, or evaluate through an appropriately constrained worker boundary.

## Success measures

- No apply-capable legacy daemon or MCP bridge can automatically replay a mutation after dispatch ambiguity.
- A caller can recover the operation ID even when the first response is lost, query status, and receive the same terminal outcome for repeated identical requests.
- Review and apply can run in separate processes and still consume byte-identical, hash-verified plan data from outside repository control.
- A symlink/reparse swap after preflight cannot redirect any write, move, delete, backup, or restore outside the verified root.
- Killing the apply owner after any journal boundary leads through lease-based reconciliation to a verified terminal state rather than indefinite `Applying`.
- Fault injection proves complete success, complete rollback, or truthful `RecoveryRequired`; partial application is never labelled `Applied`.
- No proof-strength finding or deletion suggestion is emitted from a partial workspace or an externally visible symbol without an explicit proof basis.
- Untrusted-workspace tests demonstrate that repository-controlled build logic is not executed in a privileged long-lived host.
- The five spine commands pass CLI, MCP, in-process, and passthrough parity plus cancellation and failure-injection tests.
- MCP conformance is tested for every advertised protocol version; structured tools return schema-valid `structuredContent` as well as a text view.
- `dotnet build` is hermetic: it does not modify the repository, deploy files into the user profile, or depend on an already-running daemon.
- Release verification installs and runs every supported RID, verifies checksums/provenance and rollback-safe upgrades, and either publishes the advertised NuGet tool or removes that claim.
