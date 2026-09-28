# ADR-0047: Shared doctor host services

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#56](https://github.com/DanMarshall909/roslyn-tools/issues/56), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

The reusable `DoctorOperation` consumes typed environment-probe and workspace-
bootstrap ports. Their concrete implementations still lived in the CLI assembly,
so MCP acquired them through CLI service registration. Both implementations
perform host effects: installed launcher discovery, MCP handshake, SDK probing,
and MSBuild registration.

## Decision

Move `DoctorEnvironmentProbe`, `DoctorWorkspaceBootstrap`, and their SDK
selection value into `RoslynTools.Hosting` without changing behavior. The shared
hosting assembly already owns process-global MSBuild registration. Keep the
types internal to the hosts and their tests. Pin their assembly placement in
the host boundary test.

## Consequences

- CLI and MCP can register the doctor ports without referencing CLI types.
- Core still receives only the typed ports and has no dependency on host effects.
- The probe's current default launcher resolution is unchanged; host-specific
  path selection can be addressed when each host owns its DI composition.
- MCP still references CLI for DI registration and suggestion catalogue. These
  are later #56 tracers.

## Alternatives considered

- Duplicate the probe in MCP: rejected because handshake and installation
  diagnostics would drift between hosts.
- Move the implementations into Core: rejected because process launching and
  registration are host effects.
