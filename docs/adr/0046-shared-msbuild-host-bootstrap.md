# ADR-0046: Shared MSBuild host bootstrap

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#56](https://github.com/DanMarshall909/roslyn-tools/issues/56), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

CLI and MCP both register MSBuild for semantic workspace loading. MCP called
`MsBuildBootstrap` from the CLI assembly, retaining a direct host dependency even
after SDK identity and compatibility policy moved to Core. Registration is a
process-global host effect and does not belong in the reusable application layer.

## Decision

Introduce `RoslynTools.Hosting`, a shared host-support assembly that depends on
Core. Move `MsBuildBootstrap` and its SDK-request value types there without
changing registration behavior. CLI and MCP reference this assembly directly.
Keep the types internal and grant access only to the two hosts and their tests.

## Consequences

- MCP can register and compare exact MSBuild instances without accessing CLI
  code for bootstrap behavior.
- Core remains independent of host registration and process-global effects.
- The shared hosting assembly must remain independent of both concrete hosts;
  an architecture test enforces its reference direction.
- MCP still references CLI for DI registration and doctor/suggestion adapters.
  Those dependencies remain separate #56 tracers.

## Alternatives considered

- Move registration to Core: rejected because process-global SDK registration is
  a host effect, not an application operation.
- Duplicate bootstrap in each host: rejected because SDK selection and
  registration semantics could drift between CLI and MCP.
