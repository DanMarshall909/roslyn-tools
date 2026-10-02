---
tags:
  - architecture
  - mcp
  - distribution
  - developer-experience
ticket: rt-012
severity: Medium
status: In Progress
area: architecture
owner: ""
reported: 2026-08-16
summary: "Distribute Roslyn Tools as a portable MCP server with one stable installer, standards-based hosting, and end-to-end diagnostics"
---

# Portable MCP installation and standards-based distribution

## Context

Consuming Roslyn Tools from another local repository currently assumes that the
consumer already has a compatible .NET SDK, a built Roslyn Tools checkout, and an
MCP configuration containing machine-specific executable and assembly paths.

This failed in a clean DnDan review environment in several independent ways:

- neither `rt` nor `dotnet` was available on `PATH`;
- DnDan pinned .NET SDK 10.0.301 while Roslyn Tools pinned 10.0.201;
- the installed SDK could not start without ICU until invariant globalization
  was explicitly enabled;
- DnDan's `.mcp.json` hard-coded `/usr/share/dotnet/dotnet` and a developer-local
  `/home/dan/code/roslyn-tools/.../RoslynTools.Mcp.dll` path;
- solution-aware Roslyn analysis could not load the consumer solution until its
  separately pinned SDK was also installed.

Building from an adjacent source checkout is useful for contributors, but it
should not be the normal installation contract for MCP consumers. Roslyn Tools
needs local filesystem and MSBuild access, so the preferred distribution should
be a versioned MCP Bundle if client support is adequate, with self-contained
platform release artifacts as the immediate portable baseline.

The MCP host previously implemented protocol dispatch directly and reported
protocol version `2024-11-05`. The official MCP C# SDK now owns the protocol
lifecycle and version negotiation. Spawned tests cover the supported stdio eras,
including pagination, cancellation, and malformed-JSON recovery.

## Execution topology authority

The 2026-08-16 decision to make the daemon the local execution owner is a
historical direction, superseded for the current hardening phase by
[ADR-0008](../../../adr/0008-daemon-transparent-passthrough-phase.md).
The daemon is an optional transparent typed transport adapter to the same
canonical application operation used in-process. It does not own operation
truth, repository trust, mutation policy, or recovery semantics.
[ADR-0017](../../../adr/0017-bounded-read-only-workspace-reuse.md) permits a
bounded read-only workspace lease cache for its named operations; that exception
does not restore daemon execution or mutation-policy ownership.

The accepted [product-spine design](../../../../openspec/changes/shape-pre-release-product-spine/design.md)
targets a daemon-free `0.1-preview`: CLI and MCP reach shared typed application
operations without requiring daemon availability. Portable distribution and
installation checks must follow that topology. A persistent stdio host does not
imply a daemon-owned workspace or require daemon startup; remaining experimental
daemon transport is a separate compatibility surface.

This reconciles architecture authority, not release readiness. The final
preview catalogue and packaged admission journeys remain gated by
[RT-016](rt-016-pre-release-product-spine.md) and its OpenSpec. The incomplete
protocol classification, client startup policy, and bundle-evaluation criteria
below remain open, as do the separately owned platform-hardening requirements.

## Acceptance Criteria

- [x] CI publishes versioned, self-contained CLI and MCP artifacts for each
      supported OS/architecture, including the Roslyn MSBuild build-host files.
- [x] Published artifacts include checksums and provenance suitable for an
      installer to verify before execution.
- [x] One idempotent user-scoped installer exists for POSIX systems and one for
      Windows; each detects the platform, installs atomically to a versioned
      location, updates a stable launcher, supports removal, and prints the exact
      MCP client configuration.
- [x] A normal consumer configuration invokes a stable command such as
      `roslyn-tools-mcp`; it contains no developer-specific absolute path, direct
      `.dll` path, assumed `/usr/share/dotnet` location, or shell-only environment
      expansion.
- [x] A preview persistent `rt trust add <repository>` registry lets one stable MCP
      registration authorize the same Git common-directory identity across currently
      registered task worktrees without a worktree path in client configuration.
- [x] Missing, corrupt, unreadable, unregistered, mismatched, and stale Git-object
      continuity records fail closed; MCP payloads and trust-management discovery cannot
      grant or broaden trust.
- [x] The standard installation does not require a separately installed .NET
      runtime, SDK, ICU package, source checkout, restore, or compilation merely
      to start the MCP server.
- [x] The server reports a clear, actionable diagnostic when semantic solution
      loading requires a consumer SDK that is not installed, including the
      requested version and `global.json` path.
- [x] `rt inspect doctor --installation-only` verifies the installed CLI and MCP launchers, artifact/build-host
      completeness, protocol negotiation, tool discovery, JSON schemas, stdout
      protocol purity, and separately captured stderr. Normal doctor mode additionally
      reports one-shot, persistent, or missing repository trust and optionally loads a
      supplied `.sln` or `.slnx` file after lazy MSBuild registration.
- [x] Doctor follows the MCP 2024-11-05 initialization lifecycle before discovery,
      distinguishes caller cancellation from its bounded handshake timeout, and terminates
      the spawned child in either case. SDK evidence distinguishes absent, valid, and invalid
      `global.json` requests and reports the request path and selection diagnostic.
- [x] POSIX and Windows installers run the installed launcher's installation-only
      self-check and fail closed on launcher, build-host, or MCP handshake errors. SDK
      selection is reported; it becomes fatal when an in-scope `global.json` request
      cannot be selected, while installation and MCP startup do not require a separate SDK.
- [x] The MCP host uses the official MCP C# SDK for supported protocol-version
      negotiation and legacy fallback rather than maintaining a bespoke
      lifecycle implementation. Spawned tests cover legacy `initialize` at
      `2024-11-05`, `2025-03-26`, `2025-06-18`, and `2025-11-25`, plus
      `server/discover` at `2026-07-28`; see ADR-0080.
- [x] A controlled spawned-stdio integration test proves that the doctor handshake
      accepts only valid newline-delimited MCP responses, validates discovery schemas,
      captures stderr separately, and terminates a timed-out child process. Broader server
      lifecycle coverage remains below.
- [x] Full MCP server integration tests prove that stdout contains only valid,
      newline-delimited MCP messages and that diagnostics are written to stderr.
      The spawned host tests read each response as JSON, require no extra stdout,
      and exercise invalid startup options with separately captured stderr.
- [x] Integration tests cover capability discovery, `tools/list` pagination and
      schemas, `tools/call` success and structured failure, cancellation, and
      graceful shutdown for every supported protocol era.
- [ ] The pinned MCP SDK reports `-32600` (Invalid Request) when valid JSON has
      a non-string `method`. SDK 2.2.0 currently reports `-32700` (Parse Error),
      although [JSON-RPC 2.0](https://www.jsonrpc.org/specification) reserves that
      code for invalid JSON. The spawned test characterizes this deviation;
      recheck an SDK fix before removing this gate or adding host transport logic.
- [x] A versioned `server.json` describes the repository, package or bundle,
      stdio transport, runtime requirements, and supported configuration, and is
      validated for eventual MCP Registry publication.
- [x] A repository marketplace packages a portable Agent Plugins 1.0 manifest and
      MCP configuration for `roslyn-tools-mcp`, plus a compatibility Codex manifest;
      the transport has no arguments, trust path, or machine-specific launcher path.
- [ ] Portable plugin installation enforces required-server startup and a scoped startup
      timeout. Agent Plugins 1.0 cannot currently encode Codex-specific `required` or
      `startup_timeout_sec` settings, so these remain client policy rather than bundle
      guarantees.
- [ ] An MCP Bundle is evaluated against the self-contained release layout and
      adopted when supported clients can install and update it without weakening
      local filesystem or process-boundary security.
- [x] Contributor documentation keeps source-build instructions separate from
      the shorter consumer installation path.

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-08-16 | Prefer MCP Bundle distribution, with self-contained release artifacts as the immediate baseline | Roslyn Tools must access local source and MSBuild, while consumers should not have to reproduce its runtime and build environment. |
| 2026-08-16 | Use one stable launcher in consumer MCP configuration | A stable executable contract removes SDK paths, checkout paths, shell expansion, and per-repository configuration drift. |
| 2026-08-16 | Include official MCP C# SDK migration in the distribution issue | Packaging a long-lived MCP executable should not cement the current handwritten, outdated protocol lifecycle. |
| 2026-08-16 | Make the daemon the typed local execution host; keep CLI, MCP, and IDE integrations as thin adapters | A persistent MCP bridge can reconnect to an upgraded daemon without replacing the client-facing stdio process, while all hosts share one workspace owner and structured command contract. |
| 2026-09-10 | Add a preview per-user Git common-directory trust registry | A stable MCP registration can serve newly registered task worktrees. Each request still validates its target, while startup and invocation-scoped trust remain compatible. The Git-object anchor is continuity hardening, not ADR-0011 OS physical identity. |
| 2026-09-10 | Publish a repo marketplace with a portable Agent Plugins 1.0 MCP bundle | The bundle owns a stable bare-command stdio transport and retains a compatibility Codex manifest. Portable schemas cannot yet express Codex startup-required or timeout policy, so those guarantees remain explicitly incomplete. |
| 2026-09-10 | Make installation verification an installed-launcher doctor mode | Installers now execute `rt inspect doctor --installation-only`. The CLI host owns process and SDK probing, core consumes injected typed results, and MSBuild registration occurs only after repository trust authorizes normal solution loading. |
| 2026-09-29 | Delegate stdio protocol lifecycle to the official MCP C# SDK | The five pinned-SDK protocol versions have spawned handshake, discovery, invocation, and EOF evidence. The shared tool catalogue and application ports remain host-neutral. Pagination, long-running cancellation, and wider malformed-input conformance remain open. |
| 2026-09-30 | Prove request cancellation across all supported stdio protocol versions | A controlled blocking application port confirms that `notifications/cancelled` reaches the request token, suppresses the cancelled response, and leaves the connection usable in the four legacy versions and `2026-07-28`. Pagination, schema coverage across pages, and remaining protocol edge cases still keep the integration criterion open. |
| 2026-09-30 | Page MCP tool discovery and verify all pages during installation | The stdio host returns up to 20 tools per `tools/list`, puts `prime` and `suggest_command` first, and rejects invalid cursors. Spawned tests walk the full catalogue and check schemas in all five protocol versions; installation doctor follows and validates every page. Remaining protocol edge cases keep the composite criterion open. |
| 2026-09-30 | Complete the enumerated stdio integration matrix and retain an SDK error-code gate | Spawned tests in all five advertised versions now cover discovery, complete paged schemas, successful and typed-failure calls, cancellation, malformed JSON, unknown methods, notification silence, recovery, and EOF. The 2026 era also checks version-drift rejection. A separate test records that valid JSON with `method: 7` gets `-32700` from SDK 2.2.0 rather than the JSON-RPC-required `-32600`; this unresolved classification, portable plugin timeout policy, and MCP Bundle evaluation keep RT-012 open. |
| 2026-10-03 | Reconcile portable distribution with ADR-0008 and the daemon-free preview target | The earlier daemon-as-execution-owner direction is historical. Optional typed transport and ADR-0017 bounded read-only reuse do not own canonical operations or mutation policy; RT-016 still owns final preview admission. |
