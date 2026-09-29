# ADR-0080: Official MCP SDK owns the stdio protocol lifecycle

- Status: Accepted
- Date: 2026-09-29
- Related work: RT-012

## Decision

The MCP executable uses `ModelContextProtocol.Core` for stdio framing,
initialization and discovery, version negotiation, request dispatch, cancellation,
and JSON-RPC responses. The host passes standard input and output streams to the
SDK transport. It does not maintain a second protocol loop.

The existing `ToolRegistry` remains the source of tool availability, schemas,
annotations, metadata, invocation, and structured results. The SDK handler maps
that catalogue into protocol tools and maps calls back to the same application
ports used before this migration. The SDK dependency belongs in the outer MCP
host; Core, Hosting, and Adapters do not depend on MCP protocol types.

Keep the SDK's default protocol-version selection so the host can negotiate
legacy `initialize` clients and current `server/discover` clients. Spawned-process
tests exercise each version advertised by the pinned SDK, real tool calls, stdout
purity, stderr diagnostics, and graceful EOF. Broader conformance, especially
pagination, cancellation under a long-running operation, and malformed-input
edge cases, remains an RT-012 gate rather than an assumed consequence of using
the SDK.

## Consequences

Protocol lifecycle maintenance follows the pinned SDK package. The host still
owns catalogue projection, policy, and result mapping, so those boundaries need
their own tests. The SDK consumes streams rather than `TextReader` and
`TextWriter`; test hosts use paired pipe streams to exercise the production
transport.
