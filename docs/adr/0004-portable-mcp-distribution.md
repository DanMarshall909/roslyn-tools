# ADR-0004: Portable MCP distribution

- Status: Accepted
- Date: 2026-08-16
- Stories: rt-012

## Context

The source-run MCP configuration required a matching .NET SDK, a built checkout, and machine-specific paths. That made
the server fragile for consumers and mixed contributor setup with the runtime contract. Roslyn's MSBuild workspace also
requires build-host content that a naive single-file publish can hide.

## Decision

Release `rt` and `roslyn-tools-mcp` together as self-contained, platform-specific archives. A checksum-verifying,
user-scoped installer selects the platform, installs into a versioned directory, and exposes stable launcher names.
Release CI produces attestations as provenance. MCP startup is independent of SDK discovery; SDK registration happens
when a tool operation needs Roslyn and reports the consumer's `global.json` when discovery fails.

The source-build workflow remains available to contributors. Migration from the handwritten MCP lifecycle to the
official C# SDK and MCP Bundle adoption remain explicit follow-up stages of rt-012.

## Consequences

- MCP client configuration no longer embeds a checkout, `dotnet`, or DLL path.
- Starting and discovering the server does not require a separately installed runtime or SDK.
- Semantic operations still require the SDK selected by the consumer solution.
- Release CI must build and attest every supported runtime archive.
- Installers and release layout form a compatibility contract enforced by `scripts/test-distribution.sh`.

## Alternatives considered

- Source-only execution: rejected because it transfers build, runtime, and path management to every consumer.
- Framework-dependent publishing: rejected because it retains the runtime and ICU prerequisites.
- MCP Bundle immediately: deferred until supported clients can update it without weakening local filesystem access.

## References

- Commits: pending
- Related ADRs: [ADR-0003](0003-shared-command-descriptors-and-application-ports.md)
- Related docs: [architecture overview](../architecture-overview.md), [rt-012](../issues/open/architecture/rt-012-portable-mcp-installation-and-distribution.md)
