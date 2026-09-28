# ADR-0045: Shared MSBuild instance identity for host adapters

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#56](https://github.com/DanMarshall909/roslyn-tools/issues/56), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

MCP checks that a requested solution selects the same MSBuild instance as the
one already registered in its long-lived process. The CLI daemon enforces the
same invariant. Identity, selection, and compatibility error types lived in the
CLI daemon file, forcing a non-CLI host to reference that assembly for shared
SDK policy.

## Decision

Move `MsBuildInstanceIdentity`, `MsBuildInstanceSelection`, and
`MsBuildInstanceCompatibility` into the reusable assembly. Keep their current
namespace and behavior so both host adapters use the same path normalization,
instance comparison, and restart diagnostics. Leave `DaemonMsBuildCompatibility`
in the CLI assembly as the daemon's host-specific guard.

## Consequences

- MCP can use shared SDK identity and compatibility policy without loading the
  CLI assembly for those types.
- `MsBuildBootstrap` was moved to the shared host assembly in the next #56
  slice; shared DI registration remains a later slice.
- An architecture test locks the three shared types to the reusable assembly;
  existing daemon and MCP tests cover their behavior.

## Alternatives considered

- Duplicate the comparison logic in MCP: rejected because two long-lived hosts
  would then enforce different SDK identity rules over time.
- Move the daemon guard itself into Core: deferred because its daemon-specific
  restart context and registration path remain host concerns.
