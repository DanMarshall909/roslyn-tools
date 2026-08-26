# ADR-0007: Host-neutral command contracts and complete application-port migration

- Status: Accepted
- Date: 2026-08-26
- Stories: rt-007, EP-012, US-095, US-096, US-097, US-098, US-099, US-143

## Context

ADR-0003 replaced duplicated MCP handlers with shared descriptors and typed
application ports. The first migrations proved the execution contract, but the
descriptor registry still lives in the CLI assembly and contains a
`Func<System.CommandLine.Command>`. MCP therefore references the CLI executable and
instantiates CLI commands to build tool schemas. Commands without ports still enter
`CliCommandBridge`, which invokes `App.RunAsync` and captures rendered output.

The daemon migration added a second central catalogue for routing, safety, response
types, and suggested follow-on tools. That catalogue is manually edited for each
migrated command and would become a conflict hotspot during a parallel conversion of
the remaining command surface.

Older EP-012 material prescribes one native MCP handler per command. ADR-0003 rejected
that approach because it duplicates binding, defaults, dispatch, and schema policy.
The remaining migration needs one explicit pattern before work is fanned out.

## Decision

Every ordinary command has one host-neutral application definition and exactly one
typed application port in `RoslynTools`. The definition owns:

- canonical command and normalized tool identity;
- `RtCommandInfo` user metadata;
- request and response types;
- read-only, apply-by-default, or review-by-default safety;
- daemon-preferred or in-process execution placement;
- MCP visibility; and
- structured suggested follow-on tools.

The definition contains no `System.CommandLine.Command`, CLI builder, MCP protocol
type, renderer, or console concept. Port and definition discovery is automatic; a
conversion agent must not edit a central command allow-list.

Each application port:

1. accepts one transport-neutral request record;
2. validates domain semantics;
3. owns workspace/session lifetime and calls injected services;
4. propagates cancellation;
5. returns one typed response through `Result<TResponse, RtError>`;
6. never returns a nested `Result` as a successful payload; and
7. performs no parsing, rendering, console output, exit-code mutation, or wire
   serialization.

Mutations return a shared execution envelope containing a typed domain outcome,
affected paths, and structured diagnostics. Review, dry-run, applied, and no-op are
distinct states. A review reports findings without preparing a transformation; a
dry-run prepares the transformation but does not write it.

CLI remains the owner of `System.CommandLine`, CLI discovery, argument/option binding,
terminal rendering, and exit-code projection. MCP remains the owner of JSON-RPC/stdio,
tool schema/result projection, and protocol errors. Both use generic adapters and the
same port; command-specific MCP handlers are not part of the target architecture.

MCP schemas are derived from request contracts and host-neutral property metadata,
not from instantiated CLI commands. CLI metadata is checked against the shared
definition by architecture tests.

Daemon routing follows the definition's execution placement rather than a manually
maintained daemon catalogue. Daemon recovery distinguishes:

- unavailable before dispatch, which may start/retry and safely use the same port
  in-process; and
- indeterminate after dispatch, which must not retry a mutation.

CLI and MCP use the same recovery component. Operational/bootstrap commands such as
`daemon`, `prime`, and `dump-help` remain host-owned and outside the ordinary
application catalog.

The final dependency graph is:

```text
RoslynTools.Cli ─────┐
                     ├──> RoslynTools
RoslynTools.Mcp ─────┘     ├─ application definitions and ports
                           ├─ request/result contracts
                           ├─ services
                           ├─ daemon RPC contracts/client
                           └─ Core/Roslyn primitives
```

Neither host references the other. `CliCommandBridge` is deleted, not moved into a
new shared hosting project.

## Migration sequence

1. Land the host-neutral definition, execution policy, schema, daemon recovery, and
   coverage ratchets serially.
2. Reconcile the existing ports against the final contract.
3. Convert commands in parallel using the groups in
   [the migration playbook](../command-application-port-migration.md).
4. Delete the bridge and nullable-port paths once coverage reaches every MCP-visible
   ordinary command.
5. Move shared DI registration out of CLI and remove the MCP-to-CLI project reference.

## Consequences

- CLI, MCP, daemon, and future hosts invoke the same validated operation.
- Parallel conversion agents work in command-local files without central-catalog
  conflicts.
- Schema and output drift become architecture-test failures.
- Physical CLI/MCP decoupling follows naturally after bridge deletion.
- Every command needs explicit request/result contracts, even when the legacy CLI
  returned only prose or an exit code.
- Filesystem-heavy mutations need a prepared change-set abstraction so preview and
  apply remain atomic and report every affected path.
- Existing native-handler instructions in ADR-0001 and older planning documents are
  historical; ADR-0003 and this ADR govern new work.

## Alternatives considered

- Move CLI discovery, builders, and `CliCommandBridge` into a new shared Commands
  project: rejected because MCP would still depend on CLI machinery under another
  assembly name.
- Keep a manual daemon-owned command list: rejected because it duplicates definitions
  and creates a fan-out merge hotspot.
- Add one native MCP handler per command: rejected by ADR-0003 because it duplicates
  host binding and schema logic.
- Route every port through the daemon, including feedback and file-only trace tools:
  rejected because execution placement is an explicit application policy and not all
  operations benefit from a resident workspace.

## References

- [ADR-0001](0001-core-service-thin-host-adapters.md)
- [ADR-0003](0003-shared-command-descriptors-and-application-ports.md)
- [ADR-0005](0005-daemon-centred-local-execution.md)
- [Architecture overview](../architecture-overview.md)
- [Command application-port migration playbook](../command-application-port-migration.md)
- [rt-007](../issues/open/architecture/rt-007-shared-command-descriptors.md)
