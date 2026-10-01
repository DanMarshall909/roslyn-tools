# ADR-0026: Public no-effect durable Prepare boundary

- Status: Superseded by ADR-0082
- Date: 2026-09-26
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

[ADR-0025](0025-restart-safe-dispatch-intake-handoff.md) established a restart-safe
internal handoff from an exact stored plan to `Prepared`, but no production host
constructed its stores or exposed the operation through the shared application-port
surface. A direct envelope argument would also contradict the stored-plan boundary:
canonical plans can contain source bytes, exceed normal command-line limits, and must
not be republished through process arguments for each replay.

The repository's ordinary command categories describe source inspection, analysis,
refactoring, and CI enforcement. A lifecycle operation that writes protected control
state but never writes repository bytes does not truthfully fit any of those categories.
Treating it as read-only would mislabel the effect; treating it as a refactor would
require a fake repository dry-run.

## Decision

Add one experimental host-neutral `DurablePrepareOperation` and project it as the
root-level `prepare-operation` CLI command and generic `prepare_operation` MCP tool.
The request carries only a protected-store key and canonical identity:
repository ID, plan ID/hash, expected revision, caller-known operation ID and epoch
claims, dispatch lease, and intake-attempt identity. It never carries plan bytes.
The operation loads and verifies the exact envelope from `DurableChangePlanStore`,
registers the canonical request with `DurableDispatchIntakeCoordinator`, and may reach
only the no-effect `Prepared` state.

Before opening the stored plan, the coordinator inspects any retained dispatch
identity. Another principal receives the same no-disclosure denial for exact and
different work. Different work by the retained principal conflicts without changing
the plan store, dispatch record, operation record, pins, or history. A retained exact
operation can replay or resume after new-epoch admission closes; only a genuinely new
identity is checked against the current host admission policy. Registration races are
resolved under the ledger lock before plan pinning.

Production DI constructs one process-wide local authority, plan store, ledger, and
coordinator under a fixed user-local control root outside caller-selected repository
paths. The local authority derives the principal from the Windows SID or effective Unix
UID, provisions owner-private directories/files, rejects existing permission drift,
rejects symlink/reparse boundaries and lexical repository overlap, and holds a root
lease marker during each store operation. Non-creating ledger reads are distinct from
provisioning mutations, so inspection of a denied new identity cannot create a ledger
root or lease. The capability remains `Experimental` and is limited to
`ExclusivePrincipal`.

The CLI command is a root lifecycle meta-command, not an `IRtCommand` source command.
It is nevertheless included in the canonical help/suggestion catalog and descriptor
registry. Its distinct `LocalStateWrite` safety classification projects to MCP as a
destructive local write, not a repository write or read-only action. Untrusted MCP
discovery hides local-state writers; a startup-trusted MCP host can discover and call
the same typed port. MCP request binding failures and operation failures use typed
`RtError` payloads.

This slice does **not** claim the complete platform authority described by the accepted
OpenSpec. The hourly local admission policy is not `GetOperationEpoch`, a signed token,
monotonic durable epoch issuance, quota accounting, sealing, or retirement. The local
root lease does not yet prove mount/bind-alias exclusion or provide handle-relative
store I/O against same-principal root replacement. On Unix, protection validation checks
private mode bits but does not yet verify that each existing path's owner UID equals the
effective UID. Those gaps, repository enrollment, shared authority, action-scoped
lifecycle authorization, and public status keep the surface experimental and prevent it
from serving as an apply authority. This slice therefore does not claim complete local
authority hardening.

## Consequences

- CLI and trusted MCP invoke one typed application operation and observe the same
  `Prepared`, replay, conflict, access-denied, plan-unavailable, and coordination errors.
- Plan bytes stay in the protected store; command lines and MCP requests carry only the
  immutable key and canonical operation identity.
- Every implemented persistence frontier can be interrupted and resumed by a fresh
  application composition without a repository write or duplicate lifecycle history.
- `LocalStateWrite` is now a first-class command safety state and cannot be projected as
  read-only or as a repository mutation with a fake dry-run.
- `prepare-operation` is not yet useful until a plan-producing operation stores the
  reviewed envelope and an authority-issued epoch surface replaces the local preview
  policy.
- Reaching `Prepared` still grants no repository reservation, effect permission,
  journal, rollback, recovery, or cleanup authority.

## Alternatives considered

- **Pass the canonical envelope as base64.** Rejected because argv exposes plan content,
  normal command-line limits are far below the plan limit, and rejected identities
  could publish unreferenced plans.
- **Persist the envelope before checking the operation identity.** Rejected because a
  conflicting or unauthorized request could consume plan-store capacity.
- **Classify Prepare as read-only.** Rejected because it durably writes control state.
- **Classify Prepare as a repository refactor.** Rejected because `Prepared` has no
  repository effect and a dry-run would be fictitious.
- **Claim completion of P0.2/P0.3.** Rejected because issuance, quotas, status,
  reservation, effects, journal, rollback, and recovery remain absent.

## References

- Related ADRs: [ADR-0003](0003-shared-command-descriptors-and-application-ports.md),
  [ADR-0007](0007-host-neutral-command-contracts.md),
  [ADR-0023](0023-protected-durable-change-plan-storage.md),
  [ADR-0024](0024-durable-operation-intake-ledger.md),
  [ADR-0025](0025-restart-safe-dispatch-intake-handoff.md)
- Related docs: [architecture overview](../architecture-overview.md)
- Specification: [apply operations have caller-known durable identity](../../openspec/changes/critical-platform-hardening/specs/platform-hardening/spec.md#requirement-apply-operations-have-caller-known-durable-identity)
