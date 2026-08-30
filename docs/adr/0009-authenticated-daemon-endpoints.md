# ADR-0009: Authenticated local daemon endpoints

- Status: Accepted
- Date: 2026-08-30
- Supersedes: the conditional endpoint-access-control allowance in [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

The transparent daemon accepts source-bearing requests and executes them with the
daemon user's authority. A per-user or collision-resistant endpoint name prevents
accidental collisions but does not authenticate a client or server. If endpoint ACLs
are unavailable or fail open, another local principal can invoke the daemon, read
results, or impersonate it to a client.

The optional transport is an optimization, so availability does not justify weakening
the trust boundary. Direct in-process execution remains available before dispatch.

## Decision

The daemon starts and advertises an endpoint only when it can prove an authenticated
and authorized local channel. The endpoint is per-user and collision-resistant, and
each connection authenticates both the client principal and daemon instance before
accepting a request body or returning source-bearing data.

An implementation uses OS-enforced peer identity and endpoint/container ACLs, or a
cryptographically equivalent mutually authenticated local transport whose discovery
and credentials are stored outside repository control with user-private OS/service
protection. Credentials are scoped to the user and server instance, rotated on daemon
restart, excluded from endpoint names, arguments, environment, and logs, and never
accepted from a repository or ordinary request payload. Authorization failure is
fail-closed and is reported without disclosing whether a repository or operation
exists.

If endpoint creation, discovery, credential protection, peer authentication, or
authorization cannot be proven on a platform, daemon startup fails and no endpoint is
advertised. A host may select the canonical in-process path before dispatch and report
the typed daemon-unavailable diagnostic; it never falls back after indeterminate
mutation dispatch.

## Consequences

- Collision resistance is retained for routing but is not treated as authentication.
- Unsupported platforms lose daemon transport rather than exposing an unauthenticated
  source-bearing service.
- Endpoint implementations need cross-principal denial, rogue-server, stale-credential,
  ACL-failure, restart-rotation, and no-disclosure tests.
- The daemon remains a transparent adapter and gains no workspace, trust, or mutation
  authority.

## Alternatives considered

- Permit an unauthenticated endpoint on platforms without ACL support: rejected because
  endpoint discovery would confer the daemon user's authority on another principal.
- Rely only on an unpredictable endpoint name: rejected because secrecy of a routing
  name is not durable authentication and does not authenticate the server to clients.
- Make daemon transport mandatory: rejected because fail-closed in-process execution is
  simpler and preserves the canonical operation contract.

## References

- Related ADRs: [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- Related docs: [architecture overview](../architecture-overview.md), [critical platform hardening design](../../openspec/changes/critical-platform-hardening/design.md)
