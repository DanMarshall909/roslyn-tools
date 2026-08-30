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
authority audits those changes, applies revocation before later evaluator admission,
and does not allow repository content, evaluator output, ordinary requests, or
non-interactive callers to bootstrap trust.

`Trusted | Untrusted` remains independent from `NoEvaluation | InProcess |
IsolatedWorker`. Untrusted repositories cannot select `InProcess`. An isolated worker
runs under a distinct least-privilege identity with OS-enforced process/PID,
process-handle, IPC, filesystem, and network isolation. It cannot impersonate the host
user, reach host processes or undeclared IPC, inherit ambient handles or secrets, read
undeclared host paths, write the checkout, or use network egress. Only bounded
schema-validated operation-scoped supervisor channels and declared read-only inputs
are exposed; descendants remain contained.

If any required identity, process/handle/IPC, filesystem, or egress boundary cannot be
proven, untrusted evaluation is refused. `NoEvaluation` file reads remain subject to
the separate root-contained handle-relative safe-reader contract.

## Consequences

- All hosts report the same authority-derived effective trust and evaluator mode in
  provenance.
- Trust grants are explicit security decisions rather than transport parameters.
- Worker implementations need platform-specific isolation proof and fail-closed
  capability detection.
- Adversarial tests cover forged trust claims, stale bindings, grant/revoke behavior,
  host-process access, undeclared IPC, host-file access, writes, and network egress.

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
