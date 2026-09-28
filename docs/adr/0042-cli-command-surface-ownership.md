# ADR-0042: CLI command surface belongs to the CLI assembly

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53), [#55](https://github.com/DanMarshall909/roslyn-tools/issues/55)

## Context

The reusable `RoslynTools` project referenced `System.CommandLine` because it
compiled CLI command markers, an option helper, and the `dump-help` command
surface projection. None of those types is needed to execute a typed application
port. The shared `ApplicationCommandDefinition` and descriptor records already
have no parser or command-builder types.

## Decision

Compile `IRtCommand`, its category and output markers, `CommandExtensions`, and
the `dump-help` command-surface builder and model in `RoslynTools.Cli`. Remove
the `System.CommandLine` package reference from the reusable project. Keep the
existing `RoslynTools.Core` namespaces for these moved internal types in this
slice so command source imports and the serialized command surface do not change.

An architecture test checks the package and compiled assembly references,
rejects `System.CommandLine` imports in reusable source, and checks ownership of
the CLI-only types. The generated JSON help output is compared byte-for-byte
before and after the move.

## Consequences

- Core and application services compile without `System.CommandLine`; CLI
  command construction and help projection remain in the `rt` assembly.
- Existing CLI command metadata, help shape, and category checks remain stable.
- The namespace names of the moved internal types reflect their former source
  location. Assembly ownership, rather than namespace spelling, is the boundary
  enforced by this decision.
- This does not complete the host-neutral catalogue extraction in #54, remove
  MCP's CLI project reference in #56, or close the wider #53 migration. The
  current CLI descriptor registry and compatibility bridge remain for their
  owning follow-up slices.

## Alternatives considered

- Keep `System.CommandLine` in the reusable project for help projection:
  rejected because application consumers would inherit a host dependency for
  a CLI-only function.
- Rename every moved type's namespace in the same slice: deferred because it
  changes many source imports without strengthening the assembly boundary.
