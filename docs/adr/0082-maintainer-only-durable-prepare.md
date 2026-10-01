# ADR-0082: Keep durable Prepare on the maintainer surface

- Status: Accepted
- Date: 2026-10-01
- Related OpenSpec: [pre-release product spine](../../openspec/changes/shape-pre-release-product-spine/proposal.md)
- Supersedes: [ADR-0026](0026-public-no-effect-durable-prepare.md) public host projection only

## Context

ADR-0026 projected the no-effect durable Prepare operation through root CLI and
trusted MCP discovery. The operation requires a stored canonical plan, caller-known
identity, lease, and epoch claims. The product has no public plan producer, plan
status, verified apply, or recovery lifecycle. Task suggestions therefore ranked
`prepare-operation` for requests to prepare or verify a refactor plan even though
that command cannot perform either task. Its protected local-state write also makes
ordinary discovery misleading.

## Decision

Keep the typed `DurablePrepareOperation` and descriptor for internal composition and
future lifecycle work. Move its sole CLI route to the explicit
`rt maintainer prepare-operation` group. Exclude it from public command metadata,
root help, `dump-help`, `prime`, and `suggest`; set its audience to Maintainer and
exclude it from MCP discovery and invocation. The maintainer CLI help states that
the operation durably writes protected local storage.

The underlying exact-plan verification, replay, conflict, and access-denial contracts
from ADR-0026 are unchanged. Public admission requires a complete, reviewed plan
lifecycle and truthful effect and authority contracts.

## Consequences

- Agent task discovery cannot recommend this coordination primitive as a way to
  produce or verify a change plan.
- Maintainers can exercise the existing typed operation through an explicit route.
- The generic MCP adapter retains the descriptor but does not publish or invoke it.
- CLI and MCP discovery counts each fall by one; the command census records the
  maintainer route as an ancillary surface.

## Alternatives considered

- **Keep it public and change suggestion text.** Rejected because the incomplete
  lifecycle would remain an apparently usable public operation.
- **Delete the operation and descriptor.** Rejected because the durable core contract
  and restart-safe tests remain useful for the future lifecycle.
- **Expose it to trusted MCP only.** Rejected because trust does not supply the
  missing plan producer, status, and recovery operations.

## References

- [Surface audit](../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md)
- [Architecture overview](../architecture-overview.md)
