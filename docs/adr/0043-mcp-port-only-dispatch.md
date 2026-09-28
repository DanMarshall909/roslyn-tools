# ADR-0043: MCP dispatch requires an application port

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#41](https://github.com/DanMarshall909/roslyn-tools/issues/41), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

ADR-0003 allowed a temporary `CliCommandBridge` while MCP commands acquired
application ports. The bridge translated JSON back into CLI arguments, invoked
`App.RunAsync`, and captured rendered console output. The current public MCP
surface has port coverage, so retaining that fallback permits new descriptors
to bypass typed request and result contracts.

## Decision

MCP discovery admits a descriptor only when it has an application port. The
generic MCP adapter deserializes the request into that port's request type and
invokes the port directly. Delete `CliCommandBridge`, its console-capture
adapter, and its DI registration. An architecture test checks both the port
admission rule and the absence of the bridge and capture types.

## Consequences

- A new command without an application port is unavailable through MCP until
  its port is implemented and registered.
- Existing MCP tool names, schemas, safety policy, and typed result rendering
  continue through the generic adapter.
- The MCP project still references the CLI project for descriptor registration.
  The host-neutral catalogue extraction and project decoupling remain separate
  parts of #53.

## Alternatives considered

- Keep the bridge dormant: rejected because it leaves a second invocation path
  available to future descriptors.
- Add a command-specific MCP handler for any missing port: rejected because it
  would duplicate binding and result policy instead of completing the shared
  application contract.
