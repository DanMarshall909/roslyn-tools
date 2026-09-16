## ADDED Requirements

### Requirement: Trusted read-only calls reuse a current immutable workspace
Within one MCP or daemon process lifetime, the system SHALL reuse one loaded Roslyn
workspace for sequential doctor, outline, describe, search_symbols, source, and
relationships calls with the same cache identity while freshness remains proven.

#### Scenario: sequential semantic operations share one load
- **WHEN** two named read-only semantic operations target the same trusted solution without a relevant input change
- **THEN** the system opens the solution exactly once and both operations query the same immutable snapshot revision

#### Scenario: cache restart or eviction is transparent
- **WHEN** an entry is evicted or the owning process restarts
- **THEN** the next operation performs a cold load and returns the same contract and semantic result as a clean direct load

### Requirement: Cold loads coalesce and query leases are concurrent
The system SHALL coalesce acquisition for one cold cache identity and SHALL NOT hold
the acquisition gate while a read-only semantic query uses its immutable lease.

#### Scenario: concurrent cold operations open once
- **WHEN** concurrent read-only operations acquire the same uncached identity
- **THEN** exactly one `OpenSolutionAsync` call runs and every caller receives a lease for the published snapshot

#### Scenario: read-only queries overlap safely
- **WHEN** two callers hold leases for the same published snapshot
- **THEN** both may query it concurrently and neither caller can dispose the owning workspace

### Requirement: Cache identity isolates worktrees, solutions, and trust bindings
The cache key SHALL include the canonical absolute solution, canonical worktree root,
repository trust identity, and semantic environment identity. Trust SHALL be
reauthorized before every acquisition.

#### Scenario: linked worktrees remain isolated
- **WHEN** two registered linked worktrees share a Git common directory but have different worktree roots or content
- **THEN** they receive distinct cache entries and neither observes the other worktree's snapshot

#### Scenario: solutions remain isolated
- **WHEN** two solution paths exist under one trusted worktree
- **THEN** each solution receives its own entry and revision

#### Scenario: revoked or mismatched trust cannot reuse an entry
- **WHEN** current trust authorization no longer admits the requested canonical solution and worktree binding
- **THEN** acquisition fails closed before returning a cached lease

### Requirement: Relative solutions never depend on process current directory
A relative solution path SHALL resolve only beneath an explicit configured trusted
repository root. A multi-root persistent-trust host SHALL require an absolute solution
path. Resolution SHALL reject traversal, symlink, or canonicalization outside the
trusted root.

#### Scenario: explicit-root relative path is contained
- **WHEN** a caller supplies `src/App.slnx` under an explicit trusted root
- **THEN** the system resolves it from that root regardless of the server process current directory

#### Scenario: another checkout cannot be selected relatively
- **WHEN** a relative path would escape the explicit trusted root or a persistent multi-root request supplies a relative solution
- **THEN** the request is rejected before trust admission, SDK selection, workspace loading, or cache lookup

### Requirement: Relevant input changes refresh before semantic conclusions
Before reusing an entry, the system SHALL prove a deterministic content fingerprint
covering source and semantic build inputs, analyzer/generator inputs, configuration,
and relevant environment identity. It SHALL reload when the fingerprint differs or
cannot be proven; Git HEAD alone SHALL NOT establish freshness.

#### Scenario: unstaged or untracked source change refreshes
- **WHEN** a source file is modified or a relevant untracked source file is added without changing Git HEAD
- **THEN** the next acquisition opens the solution again and exposes a new matching snapshot revision

#### Scenario: build or analyzer input change refreshes
- **WHEN** a solution, project, props/targets, global.json, analyzer/generator, configuration, metadata reference, or relevant environment input changes
- **THEN** the next acquisition reloads before returning a semantic lease

#### Scenario: freshness proof fails closed
- **WHEN** a relevant input cannot be enumerated, opened, or content-hashed consistently
- **THEN** the system does not report a cache hit and performs a clean reload or returns the existing typed load failure

### Requirement: Retention is bounded and disposal respects active leases
The system SHALL bound current cache entries, retire evicted or refreshed entries, and
dispose each owning workspace only after its final active lease ends.

#### Scenario: eviction waits for an active query
- **WHEN** an entry is evicted while a query lease remains active
- **THEN** no new lease uses that entry and its workspace remains undisposed until the active lease is released

#### Scenario: shutdown retires every entry
- **WHEN** the MCP or daemon service provider shuts down
- **THEN** every cached entry is retired and each workspace is disposed immediately or after its last active lease

### Requirement: Completeness, trust, and mutation boundaries remain unchanged
Cached acquisition SHALL preserve workspace completeness, confidence, diagnostics,
cancellation, and revision binding. Partial snapshots SHALL remain partial and SHALL
NOT authorize proof-like deletion or mutation. Mutation operations SHALL remain on
their existing in-process safe path.

#### Scenario: partial load stays partial
- **WHEN** a cached workspace has completeness-impacting diagnostics
- **THEN** every operation reports the same partial state and limitations as a clean direct load

#### Scenario: mutation is never dispatched through the cache
- **WHEN** a mutation command is invoked
- **THEN** its placement, daemon rejection, no-retry, and indeterminate-dispatch behaviour are unchanged and no cached read-only lease is used

### Requirement: Fallback is permitted only before dispatch
The MCP/CLI host SHALL execute the direct path only when the daemon transport proves
`UnavailableBeforeDispatch`. It SHALL NOT execute the operation again after
`IndeterminateAfterDispatch`.

#### Scenario: daemon loss before dispatch falls back
- **WHEN** the daemon is unavailable before any request bytes are accepted and cannot be started
- **THEN** the host executes the same read-only application port directly once

#### Scenario: dispatch becomes indeterminate
- **WHEN** request dispatch starts but no terminal daemon response is available
- **THEN** the host surfaces the indeterminate result and does not invoke the direct application port

### Requirement: Reuse and revision evidence is deterministic
The system SHALL emit bounded cache observations that distinguish load, hit, refresh,
and eviction and bind each observation to a hashed cache identity, source revision,
and monotonic process-local load count. Public semantic results SHALL expose the
snapshot revision through their existing source identity or a small additive field.

#### Scenario: black-box workflow proves reuse
- **WHEN** one real MCP process runs doctor, outline, describe, source, and relationships against a representative five-project trusted solution
- **THEN** instrumentation records one solution load and the returned results reference the same valid snapshot revision

#### Scenario: black-box source edit proves refresh
- **WHEN** the fixture source changes after the warm workflow
- **THEN** the next call records a refresh and load-count increment and returns the new snapshot revision
