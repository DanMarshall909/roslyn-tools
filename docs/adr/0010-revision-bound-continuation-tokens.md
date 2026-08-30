# ADR-0010: Revision-bound continuation tokens

- Status: Accepted
- Date: 2026-08-30
- Extends: [ADR-0007](0007-host-neutral-command-contracts.md)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

Deterministic ordering does not make separately evaluated pages one coherent result.
If source, workspace inputs, filters, projection, or ordering change between page
requests, an offset or cursor can skip, duplicate, or combine findings from different
revisions. A continuation token can also become a forged query or an accidental bearer
authorization if its integrity and authorization semantics are unspecified.

## Decision

Every continuation token is an opaque, integrity-protected reference to one canonical
request and result revision. It binds the schema and operation identity, canonical
request hash with the token omitted, canonical repository/workspace identity, source
revision and semantic-input/evaluator fingerprint, projection, filters, ordering and
tie-break rules, page-size policy, cursor position, and bounded expiry.

Each continuation request is authorized again; the token is not a bearer grant and
cannot elevate repository trust or access. The application operation validates the
token before evaluating or returning data. It either reads from the exact immutable
snapshot retained under bounded lifetime and storage quotas, or rejects the token with
a typed stale/expired/invalid-continuation error when that snapshot or binding is no
longer available. It never silently reevaluates a later workspace revision under the
old cursor.

Ordering uses stable item identities and a total tie-break rule. Identical concurrent
continuation requests are idempotent. If an operation cannot retain or reconstitute the
bound revision safely, it returns deterministic truncation with an explicit limitation
marker and no resumable token.

## Consequences

- Multi-page output represents one request and revision or fails explicitly.
- Snapshot retention, token lifetime, page size, and aggregate retained bytes need hard
  quotas and observable expiry.
- Hosts project the same paging contract; transport-specific offsets are not the
  canonical continuation state.
- Tests must mutate source and semantic inputs between pages and cover token tampering,
  authorization/trust changes, expiry, concurrent reuse, and quota exhaustion.

## Alternatives considered

- Return a bare offset: rejected because it cannot bind source revision or query shape.
- Re-run against the latest checkout: rejected because pages could silently mix
  revisions and corrupt the assembled result.
- Retain snapshots without bounds: rejected because clients could exhaust disk or
  memory by abandoning continuations.

## References

- Related ADRs: [ADR-0007](0007-host-neutral-command-contracts.md)
- Related docs: [architecture overview](../architecture-overview.md), [critical platform hardening design](../../openspec/changes/critical-platform-hardening/design.md)
