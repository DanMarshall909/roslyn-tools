# ADR-0069: Symbol lookup receives suggestion policy per request

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0068](0068-workspace-load-diagnostics-via-host-logging.md)

## Context

Core's `SymbolLocator` read `RtContext.NoSuggest` when it built a not-found
message for a member lookup. This made a reusable symbol query depend on a
global CLI flag.

## Decision

Give the lookup an explicit `includeSuggestions` argument, defaulting to the
existing suggestion behavior for direct callers. Carry the choice through
`InspectService` and the typed `DescribeRequest`: the CLI maps its
`--no-suggest` flag to `NoSuggest`, and the daemon and descriptor-driven hosts
use the same request field. Add a source test that rejects `RtContext` access
in `SymbolLocator` and a dispatcher test that checks the serialized request
on both sides of the choice.

## Consequences

- Core symbol lookup no longer reads host presentation state.
- CLI `--no-suggest` suppresses the tail during local or daemon execution;
  direct callers and descriptor-driven hosts can opt out explicitly.
- A daemon from an older build must be restarted to honor the new optional
  request field; mixed-build daemon negotiation remains separate work.
- The remaining `RtContext` implementation has no Core callers and can move
  to a host assembly in a separate tracer.
