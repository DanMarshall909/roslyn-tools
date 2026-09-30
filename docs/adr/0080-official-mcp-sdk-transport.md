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
purity, stderr diagnostics, and graceful EOF. A controlled stdio test also proves
request cancellation under a blocked application port in every supported era.
Spawned tests in every era also exercise malformed JSON, unknown methods,
typed tool failures, notification silence, recovery, and graceful EOF. The
current `server/discover` era additionally rejects a request that changes its
negotiated protocol version. These tests establish the enumerated stdio
integration behaviors; they do not claim coverage of optional MCP features.

One pinned SDK 2.2.0 deviation remains: a valid JSON object with a non-string
`method` is an invalid JSON-RPC Request and should receive `-32600` (Invalid
Request), but the SDK emits `-32700` (Parse Error, reserved for invalid JSON)
under the [JSON-RPC 2.0 specification](https://www.jsonrpc.org/specification).
The spawned test characterizes this observed behavior. RT-012 keeps its
standards-correct classification unresolved rather than adding a second protocol
parser to this host.

As of 2026-09-30, the MCP host returns `tools/list` in pages of at most 20.
`prime` and `suggest_command` lead the first page so agents can discover the
session entry points with one bounded response. A count-bound cursor advances
through the stable catalogue; invalid or stale cursors produce JSON-RPC invalid
params. The installation doctor follows every cursor, validates each page's
schemas and unique tool names, and fails closed on repeated cursors or an
unfinished page sequence.

## Consequences

Protocol lifecycle maintenance follows the pinned SDK package. The host still
owns catalogue projection, policy, and result mapping, so those boundaries need
their own tests. The SDK consumes streams rather than `TextReader` and
`TextWriter`; test hosts use paired pipe streams to exercise the production
transport.
