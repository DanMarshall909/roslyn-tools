# ADR-0058: Daemon transport belongs to shared Hosting

- Status: Accepted
- Date: 2026-09-28
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Completes: The source-placement follow-up in [ADR-0057](0057-host-dependency-and-contract-ratchets.md)

## Context

ADR-0057 enforced project and compiled-reference boundaries, but the reusable
Core assembly still contained the named-pipe RPC client, process launcher,
recovery coordinator, server dispatcher, protocol DTOs, and a daemon-specific
legacy command catalogue. No project reference could reveal host transport code
already inside Core.

## Decision

Move the daemon RPC implementation and launcher from Core to shared Hosting,
which CLI, MCP, and DaemonHost already reference. Move the legacy daemon command
catalogue to Hosting as well; leave canonical application definitions and the
host-neutral execution result in Core. Preserve namespaces and protocol shapes
so the three adapters keep their existing contracts. Extend the architecture
ratchet to require these transport and catalogue types in Hosting and reject
named-pipe code under the Core source tree.

## Consequences

- Reusable Core no longer contains named-pipe client code, daemon process
  launching, RPC dispatch, recovery, or the legacy daemon routing inventory.
- Host adapters share one transport implementation through Hosting without
  referencing one another. CLI packaging and public command surfaces stay the
  same.
- Core still carries host-neutral execution placement metadata, including
  `DaemonPreferred`; it does not carry the daemon transport implementation.

## Alternatives considered

- Leave internal transport types in Core because they have no CLI package
  reference: rejected because source placement would still couple the reusable
  application assembly to a host implementation and evade ADR-0057's ratchet.
