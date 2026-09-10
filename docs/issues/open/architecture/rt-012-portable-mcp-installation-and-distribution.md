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

The MCP host also implements protocol dispatch directly and reports protocol
version `2024-11-05`. Distribution work is the natural point to move protocol
lifecycle, version negotiation, cancellation, schemas, and structured errors to
the official MCP C# SDK rather than extending the handwritten transport.

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
- [x] POSIX and Windows installers run the installed launcher's installation-only
      self-check and fail closed on launcher, build-host, or MCP handshake errors. SDK
      selection is reported; it becomes fatal when an in-scope `global.json` request
      cannot be selected, while installation and MCP startup do not require a separate SDK.
- [ ] The MCP host uses the official MCP C# SDK for supported protocol-version
      negotiation and legacy fallback rather than maintaining a bespoke
      lifecycle implementation.
- [x] A controlled spawned-stdio integration test proves that the doctor handshake
      accepts only valid newline-delimited MCP responses, validates discovery schemas,
      captures stderr separately, and terminates a timed-out child process. Broader server
      lifecycle coverage remains below.
- [ ] Full MCP server integration tests prove that stdout contains only valid,
      newline-delimited MCP messages and that diagnostics are written to stderr.
- [ ] Integration tests cover capability discovery, `tools/list` pagination and
      schemas, `tools/call` success and structured failure, cancellation, and
      graceful shutdown for every supported protocol era.
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
