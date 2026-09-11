# ADR-0011: Authoritative repository trust and evaluator isolation

- Status: Accepted
- Date: 2026-08-30
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

Loading an MSBuild workspace can execute repository-controlled tasks, analyzers,
generators, and child processes. A `Trusted` field supplied by a CLI, MCP, or daemon
request is untrusted input; accepting it as authority lets an adapter elevate a
repository without a user decision. Filesystem and network containment are also
insufficient when worker code shares the host user's process and IPC boundary.

Trust and isolation answer different questions: whether the authenticated user has
explicitly authorized host-level evaluation for this repository, and where evaluation
will execute. Neither can be inferred from locality or the other axis.

## Decision

Effective repository trust is derived at the application boundary from an
OS/service-protected authenticated-principal-scoped registry outside repository
control. Records bind the canonical repository identity to its verified physical
binding. A request-carried trust value is an expected-state or downgrade constraint,
never authority to grant or elevate trust. Missing, revoked, unreadable, mismatched, or
stale-binding records resolve to `Untrusted`.

Only authenticated `GrantRepositoryTrust` and `RevokeRepositoryTrust` operations
backed by deliberate user action through a trusted surface can change a record. The
authority audits those changes and does not allow repository content, evaluator
output, ordinary requests, or non-interactive callers to bootstrap trust.

The authority enforces global and per-principal count/byte quotas for current and hot
audit records and rate/concurrency limits for grants. A grant reserves its own state,
audit transition, and worst-case eventual revocation/compaction capacity before it can
persist `Trusted`. Saturation returns `TrustRegistryCapacityExceeded` and leaves the
binding untrusted; revocation, status, termination, and reconciliation remain available
and an already-untrusted revoke is idempotent.

Every transition receives a monotonic never-reused trust-decision sequence. After a
revocation is complete, old-generation execution is proven quiescent, and bounded hot
retention elapses, the authority atomically folds its record and audit digest into a
fixed-size authenticated accumulator and advances a protected compaction watermark
before removing detail. Missing compacted state remains `Untrusted`; restored decisions
at or below the watermark are rejected, and only a later explicit grant can establish
trust. Active trusted/pending records and unquiesced generations remain pinned. Startup
and restore must prove continuity of the authority generation, sequence high-water
mark, watermark, and accumulator; otherwise grants are refused and evaluation is
untrusted rather than treating the authority as fresh.

Grant, revoke, and `InProcess` evaluator admission share one linearizable gate for the
principal and verified repository binding. Admission atomically revalidates the grant
and acquires a revocable lease for the current monotonic trust generation plus a held
verified-root capability before any repository-controlled code runs. The evaluator's
actual solution/build open consumes that handle-relative or immutable snapshot view,
not a re-resolved ambient path; a host that cannot preserve the binding through startup
refuses `InProcess`. Revoke closes admission, advances the generation,
persists `Untrusted`, and then cancels or terminates already admitted old-generation
evaluations and descendants. It completes only after their quiescence is verified;
until then it is durably `RevocationPending` and replacement grants remain fenced.
Missing lease state fails closed and requires reconciliation.

Every `InProcess` host and process it can create is pre-contained in a non-breakaway OS
descendant domain owned by an external supervisor that executes no repository code.
The supervisor retains the kill capability and durable status. Revocation terminates a
per-admission domain when attribution is proven, otherwise the whole containing host,
and completes only after the OS proves the revoked generation has no live descendant.
Platforms unable to prove containment, external termination, and empty-domain
quiescence refuse `InProcess`.

`Trusted | Untrusted` remains independent from `NoEvaluation | InProcess |
IsolatedWorker`. Untrusted repositories cannot select `InProcess`. An isolated worker
runs under a distinct least-privilege identity with OS-enforced process/PID,
process-handle, IPC, filesystem, and network isolation. It cannot impersonate the host
user, reach host processes or undeclared IPC, inherit ambient handles or secrets, read
undeclared host paths, write the checkout, or use network egress. Only bounded
schema-validated operation-scoped supervisor channels and declared read-only inputs
are exposed; descendants remain contained.

Checkout inputs are opened relative to a verified root and validated by final handle
before content is consumed. Multiply linked files are rejected unless every alias is
proven inside declared roots. Accepted content is materialized onto private inodes, or
the platform must prove equivalent alias containment for an immutable mount, so a
repository hard link cannot expose an ambient host file.

Worker admission reserves aggregate capacity. OS-enforced descendant-inherited limits
cover process/task and thread creation, process-table entries, handles/file
descriptors, CPU, memory, wall-clock time, and IPC/result bytes. Disposable writable
filesystems have byte and inode/file-count quotas. Terminal and reconciliation paths
terminate descendants and destroy or quarantine writable namespaces within a bounded
deadline; capacity is unavailable until cleanup is verified.

If any required identity, process/handle/IPC, filesystem, or egress boundary cannot be
proven, untrusted evaluation is refused. `NoEvaluation` file reads remain subject to
the separate root-contained handle-relative safe-reader contract.

## Consequences

- All hosts report the same authority-derived effective trust and evaluator mode in
  provenance.
- Trust grants are explicit security decisions rather than transport parameters.
- Trust-state and audit storage is quota-bounded; grant admission reserves revocation
  capacity, and compacted absence remains fail-closed untrusted.
- Revocation is linearizable with evaluator admission and has an explicit quiescence
  contract for work admitted before the revocation fence.
- Trusted in-process execution still requires non-escapable descendant containment;
  revocation may terminate the containing host to make its quiescence claim truthful.
- Worker implementations need platform-specific isolation proof and fail-closed
  capability detection.
- Read-only checkout views cannot preserve unproven host inode aliases, and process,
  descriptor, writable-byte, inode, and teardown budgets are containment properties.
- Adversarial tests cover forged trust claims, stale bindings and startup replacement,
  grant/revoke admission races, quota saturation/churn, audit compaction/rollback,
  lost-lease reconciliation, hard-linked host files,
  fork/descriptor and disk/inode exhaustion, cleanup failure, host-process access,
  undeclared IPC, host-file access, writes, and network egress.

## Implementation status

The authoritative protected registry, OS-verified physical binding, audit/quotas,
evaluator leases, revocation, and isolated worker remain unimplemented. The
`0.1-preview` trusted-local implementation persists deliberate `rt trust add` decisions
in the current user's local application-data directory. Records are keyed by canonical
Git common-directory path and carry a root-commit object anchor. Every CLI, MCP, or
daemon admission verifies that the requested solution remains beneath a currently
registered worktree with the matching common directory and retained anchor. Missing,
corrupt, unreadable, unregistered, mismatched, or anchor-stale state fails closed.
Persistent daemon admission resolves the effective request target and verifies that
registry binding before its SDK-context guard inspects repository-controlled SDK
selection inputs. Malformed or untrusted targets therefore fail as `TrustRequired`
without reaching SDK inspection. Invocation-scoped trusted CLI commands remain an
in-process compatibility path rather than request-carried daemon authority.

The anchor is a best-effort repository-self-asserted continuity check, not the verified
handle/inode/file-ID binding required by this ADR; a replacement repository containing
the same object can inherit the preview record. POSIX storage is atomically replaced
under an exclusive mutation lock and restricted to user-only modes. Windows currently
relies on inherited per-user Local AppData ACLs rather than installing and verifying an
explicit DACL. Invocation-scoped CLI approval and startup-scoped MCP approval remain as
compatibility paths. Ordinary MCP tool arguments cannot grant or broaden trust, and MCP
never exposes trust-management operations. This makes the preview semantic MCP surface
usable without representing the P0.1 trust and isolation programme as complete.
On Unix, the preview resolves existing symlink path components to final physical paths
before Git containment and identity checks. Broken links, loops, and escapes fail
closed, but this path-based check remains subject to TOCTOU races and is not the
OS-verified handle/inode/file-ID binding required by this ADR.

## Alternatives considered

- Trust the request field: rejected because an adapter could self-authorize privileged
  repository evaluation.
- Infer trust from a local checkout or open editor: rejected because locality is not a
  user authorization decision.
- Sandbox only filesystem and network access: rejected because same-principal process
  handles and IPC remain an exfiltration and control channel.
- Best-effort isolation: rejected because an unproven boundary must not be represented
  as safe untrusted evaluation.

## References

- Related docs: [architecture overview](../architecture-overview.md), [critical platform hardening design](../../openspec/changes/critical-platform-hardening/design.md)
