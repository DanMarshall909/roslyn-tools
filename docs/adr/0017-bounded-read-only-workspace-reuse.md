# ADR-0017: Bounded read-only workspace reuse

- Status: Accepted
- Date: 2026-09-16
- Supersedes in part: [ADR-0008](0008-daemon-transparent-passthrough-phase.md) only for bounded workspace reuse by the named read-only semantic operations
- Related OpenSpec: [read-only workspace reuse](../../openspec/changes/daemon-read-only-workspace-cache/proposal.md)

## Context

ADR-0008 correctly parked the earlier daemon cache sketch because Git HEAD, dirty
state, and index timestamps could not prove semantic freshness, and because an
unbounded daemon-owned workspace risked crossing trust, mutation, worktree, and
disposal boundaries. The current MCP workflow nevertheless opens the same trusted
solution for each doctor and navigation request even though the daemon and MCP host
already have a bounded process lifetime and the semantic operations consume immutable
Roslyn `Solution` snapshots.

The ordinary workflow needs no generic session: every request already names its
solution and carries the existing trust admission context. A narrow read-only lease
cache can therefore remove repeated solution loads without giving the daemon mutation
authority or weakening completeness and confidence.

## Decision

The process hosting canonical application ports may retain at most four current
read-only workspace entries for doctor, outline, describe, search_symbols, source,
and relationships. Each entry is addressed by canonical absolute solution path,
canonical worktree root, repository trust identity, and semantic environment identity.
Linked worktrees never share an entry merely because they share a Git common directory.

Every acquisition reauthorizes current repository trust before cache lookup. Relative
solution paths resolve only beneath an explicit trusted repository root; a persistent
multi-root host requires an absolute solution. Canonicalization, traversal, symlink,
or trust-root escape fails before evaluation.

An entry stores an immutable Roslyn solution, its owning workspace, source revision,
and a deterministic content fingerprint of the semantic inputs declared by the
accepted OpenSpec. This includes dirty and untracked source, solution/project and
props/targets/configuration files, relevant `global.json`, loaded additional and
analyzer-config documents, analyzer/generator and metadata inputs, and the allow-listed
environment/MSBuild identity. Literal imports and referenced files outside the
worktree are followed, and applicable directory configuration is discovered through
the ancestor chain. Solution-declared project paths are discovered before loading so
external project files and their literal import graphs participate in the load
stability check. Dynamic, wildcard, or deliberately skipped generated import graphs
that cannot be enumerated make the resulting entry non-reusable. Imports with an
unsupported file role or context-dependent project-directory expansion are likewise
non-reusable rather than resolved approximately. A loaded project
outside the trusted worktree is likewise non-reusable because a newly added file in
its implicit SDK compile glob cannot be inferred from the previous snapshot. Git HEAD and
timestamps alone are never freshness proof. The repository fingerprint is checked
before load, after load, and again after the full snapshot fingerprint. Loaded source,
additional, and analyzer-configuration document text is also compared with a stable
physical read before publication and on every hit. These checks prevent newly changed
physical inputs from being paired with an older loaded solution. If enumeration,
reading, canonicalization, hashing, or loaded-text comparison cannot prove the current
input set, the request reloads instead of consuming the entry.

Each key has one asynchronous acquisition gate. Concurrent cold or stale callers
publish one load, then receive independent read-only leases; the gate is not held
during semantic queries. Refresh, least-recently-used eviction, and shutdown
coordinate with the per-key gate before retiring a published entry. Retirement is
serialized asynchronously so a competing acquisition cannot be synchronously
deadlocked. A waiter rechecks that its slot is still published after acquiring the
gate and retries against the replacement slot after eviction. A waiter that observes
shutdown retires any entry it removes, so it cannot orphan the workspace ahead of the
shutdown sweep. A retired workspace is
disposed only after its final lease ends. Process shutdown retires every entry.

The cache is exposed through a separate read-only workspace-session interface.
Mutation operations retain `IWorkspaceSessionFactory`, remain `InProcess`, and remain
rejected by daemon dispatch. Partial workspace state, diagnostics, confidence,
cancellation, and source-revision binding flow unchanged from the leased snapshot.
Direct fallback is permitted only after proven `UnavailableBeforeDispatch`; an
indeterminate dispatched request is not executed again in-process.

Bounded telemetry records load, hit, refresh, and eviction with a hashed identity,
source revision, and process-local load count. It records no source text, raw request,
credential, or environment value. Existing MCP arguments remain unchanged. Results
reuse existing source-identity revision fields or add only `sourceRevision` where the
legacy result had no revision field.

## Consequences

- Sequential read-only MCP calls can reuse one expensive MSBuild/Roslyn load while
  returning revision-consistent results.
- Freshness validation adds bounded I/O; correctness wins over a hit when proof is
  unavailable.
- Active queries can temporarily keep retired workspaces alive, but no retired entry
  receives a new lease and the number of current entries remains bounded.
- Cache eviction, refresh, or daemon restart changes latency, not semantics or public
  invocation shape.
- The stronger evaluator containment and revocable-lease guarantees in ADR-0011
  remain future work; this slice preserves the existing trusted-local preview boundary
  and does not represent it as complete.
- Generic daemon sessions, durable/cross-machine caches, incremental workspace
  mutation, hot-upgrade draining, and daemon-owned mutation remain prohibited.

## Alternatives considered

- Cache by Git HEAD plus dirty state: rejected because unstaged, untracked, linked,
  generated, analyzer, import, configuration, and environment changes can be missed.
- Put caching in the existing workspace factory: rejected because mutation callers
  share that seam and could accidentally consume cached state.
- Require clients to pass a session token: rejected because solution and trust identity
  already define the request and hidden workflow state would change the user contract.
- Keep reopening on every request: rejected because deterministic instrumentation can
  prove safe reuse and the repeated load dominates the normal navigation workflow.

## References

- Related ADRs: [ADR-0008](0008-daemon-transparent-passthrough-phase.md), [ADR-0009](0009-authenticated-daemon-endpoints.md), [ADR-0011](0011-authoritative-trust-and-evaluator-isolation.md), [ADR-0015](0015-operation-lifecycle-authorization.md)
- Related docs: [architecture overview](../architecture-overview.md), [stage-2 workspace caching](../../tasks/daemon-stage2-workspace-caching.md)
