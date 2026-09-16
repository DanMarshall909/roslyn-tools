## Context

The CLI, MCP host, and daemon share typed application ports and one singleton
`WorkspaceLoader` per process. Each read-only operation currently asks that loader
for a workspace, then disposes the returned workspace; the loader therefore opens
the same solution again for the next request. The persistent daemon is currently a
transparent transport and intentionally rejects every mutation port.

ADR-0008 parked all workspace ownership until a separate proposal defined identity,
freshness, trust, lifetime, concurrency, and failure behaviour. This change supplies
that narrower decision for six existing read-only semantic operations only. The
preview trust registry remains authoritative and the existing completeness and
confidence policy still evaluates every acquired snapshot.

## Goals / Non-Goals

**Goals:**

- Reuse one immutable Roslyn `Solution` and its owning `Workspace` across sequential
  read-only calls while its complete semantic-input fingerprint remains unchanged.
- Coalesce concurrent cold or stale acquisition for one identity into one
  `OpenSolutionAsync` call and permit concurrent leases after acquisition.
- Bind identity to the canonical solution path, canonical worktree root, repository
  identity, and semantic environment.
- Refresh conservatively on source/build/configuration/analyzer/generator/environment
  change, or when freshness cannot be proved.
- Bound retained entries and defer workspace disposal until the final active lease
  ends.
- Preserve public invocation shapes while exposing the snapshot revision in the
  existing source identity or a small additive `sourceRevision` field.

**Non-Goals:**

- Daemon-owned mutation, prepared-plan execution, retry after indeterminate dispatch,
  workflow sessions, cross-machine state, durable cache persistence, incremental
  workspace mutation, or a general repository index.
- Claiming completeness from a partial workspace, changing confidence policy, or
  making the preview trust registry satisfy the stronger future isolation ADR.
- Hot daemon upgrade/draining, multi-user scheduling, or the broader daemon roadmap.

## Decisions

### Use a separate read-only lease seam

The six operations depend on an `IReadOnlyWorkspaceSessions` abstraction that returns
an immutable solution lease with its source revision. Mutation operations continue to
depend on `IWorkspaceSessionFactory` and retain their existing load/apply path. This
makes the safety boundary structural rather than a runtime flag.

Alternative: cache inside the existing loader for every caller. Rejected because
mutation operations could accidentally consume a cached snapshot and because current
callers dispose the returned workspace directly.

### Resolve and authorize identity before cache lookup

Acquisition canonicalizes the solution, revalidates the active trust scope, and
derives `(repository identity, canonical worktree root, canonical solution path,
semantic environment identity)`. The worktree root remains part of the key even when
linked worktrees share one Git common directory. Relative solutions are accepted only
when an explicit trusted root exists and are resolved beneath that root; persistent
multi-root MCP/daemon requests must be absolute. Escapes and unresolved trust fail
before cache access.

Alternative: key by Git HEAD or common directory. Rejected because dirty/untracked
content and linked worktrees can differ at one HEAD.

### Prove freshness with a conservative semantic-input fingerprint

Each loaded entry stores a deterministic content fingerprint over:

- the solution, project, source, additional, analyzer-config, props/targets,
  configuration, and generated semantic files beneath the worktree;
- every loaded document outside the worktree plus analyzer/generator and metadata
  reference inputs needed by the snapshot;
- applicable `global.json` and directory build/package configuration; and
- the allow-listed environment and registered MSBuild/SDK identity that influence
  evaluation.

Files are content-hashed, not identified by Git HEAD or timestamp alone. Directory
enumeration and reads are fail-closed: an unreadable, disappearing, unsupported, or
otherwise unprovable input makes the entry stale and forces a clean reload. The
fingerprint follows literal imports and declared inputs outside the worktree and
walks ancestor directories for applicable build/package/SDK configuration. A dynamic
import, wildcard, or skipped generated import graph that cannot be enumerated marks
the entry non-reusable, so later calls reload instead of claiming a hit. The same
fail-closed rule applies to unsupported imported-file roles and project-context
properties encountered after leaving the owning project file. Project paths
declared by the solution are discovered before load, including external projects and
their literal imports. A loaded project outside the trusted worktree is conservatively
non-reusable because its implicit SDK compile glob can acquire a new file not present
in the loaded snapshot. Repository inputs are captured before load, after load, and
again after the full snapshot capture. The full capture compares every loaded source,
additional, and analyzer-configuration document with a stable physical read. A change
during that interval or a loaded/physical text mismatch retries the load before
publication. The same comparison guards warm hits. Newly added relevant files under
the worktree are included, so unstaged and untracked source changes refresh.

Alternative: filesystem watchers plus Git dirty state. Rejected because watcher loss,
timestamp granularity, untracked inputs, and external analyzer/configuration inputs
can serve stale semantics.

### Serialize acquisition, not queries

Each key owns an asynchronous refresh gate. The first caller loads and publishes one
entry; concurrent cold callers await that same gate and lease the published entry.
Warm callers validate under the gate, then release it before semantic work. A stale
replacement retires the old entry, but disposal waits for its active lease count to
reach zero. Eviction and shutdown acquire the same per-key gate, under a serialized
retirement coordinator, before removing or retiring an entry; they therefore cannot
orphan a cold or refreshed publication or retire an entry between validation and
lease creation. A caller that was already queued on a slot removed by eviction
rechecks publication under that slot's gate and retries against the replacement
instead of leasing the retired entry. A queued caller that observes shutdown retires
any entry it removes before rejecting the acquisition.

### Use bounded least-recently-used retention

The process retains at most four current entries by default. Eviction removes the key
immediately, retires its workspace, and defers disposal while leases remain active.
Daemon/MCP shutdown retires all entries. Active leases may temporarily keep retired
workspaces alive, but no retired entry can receive a new lease.

### Keep daemon dispatch fail-closed

Doctor, outline, describe, search_symbols, source, and relationships become
`DaemonPreferred`; mutation definitions remain `InProcess`, and the dispatcher keeps
rejecting mutation ports. Direct fallback occurs only for
`UnavailableBeforeDispatch`. `IndeterminateAfterDispatch` never invokes the operation
again in-process. The existing request and response shapes remain unchanged except
for additive snapshot revision fields.

### Emit bounded operational observations

The cache reports `load`, `hit`, `refresh`, and `evict` observations containing a
hashed cache identity, snapshot revision, and monotonic process-local load count.
Tests inject an observer; the daemon writes the same bounded metadata to its existing
diagnostic log. No source text, raw request, trust credential, or environment value is
logged.

## Risks / Trade-offs

- [Fingerprinting can consume noticeable I/O on large repositories] → hash only
  declared semantic input classes, reuse the immutable snapshot after validation,
  measure the representative fixture, and reload rather than weaken the proof.
- [An external writer can change files immediately after validation] → acquisition
  defines the snapshot linearization point; a lease always queries one immutable
  Roslyn solution and exposes that revision, while the next acquisition refreshes.
- [Eviction races a long query] → retirement prevents new leases and reference
  counting defers disposal until the last query releases.
- [Trust is revoked while a lease is active] → every acquisition reauthorizes;
  this slice does not claim the future revocable evaluator containment described by
  ADR-0011, and records that residual preview limitation unchanged.

## Migration Plan

1. Add the lease/cache/fingerprint seam and deterministic RED tests.
2. Move only the six read-only operations to leases and daemon-preferred placement.
3. Add path-admission, fallback, mutation-boundary, revision, and telemetry tests.
4. Run one spawned MCP/daemon fixture through the five semantic calls, modify source,
   and verify one cold load followed by one refresh.
5. Roll back by reverting this slice; cache eviction or process restart already
   produces the pre-change cold-load behaviour without changing results.

## Open Questions

None for this bounded slice. Broader evaluator isolation, hot upgrade draining,
durable sessions, and daemon-owned mutation remain separate decisions.
