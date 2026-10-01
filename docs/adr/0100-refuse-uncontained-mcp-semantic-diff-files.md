# ADR-0100: Refuse uncontained MCP semantic-diff file reads

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0089](0089-honest-working-tree-read-platform-support.md), [ADR-0090](0090-route-working-tree-review-with-platform-admission.md)

## Context

The shared semantic-diff operation accepts two source files without Git or a
solution. The local CLI can invoke that mode, but the persistent MCP adapter
cannot resolve its repository trust target and returns an unrelated absolute
workspace-target error. Bypassing workspace admission would make MCP read any
caller-named host paths through the current `File.ReadAllText` adapter. It would
not satisfy the accepted root-contained, no-follow file-read contract, even
when the host has startup trust for a different repository.

## Decision

The MCP adapter rejects every nonempty `files` request before daemon forwarding,
workspace admission, or MSBuild setup with a typed `Unsupported` error. It explains that contained
file reading is unavailable and points to the local CLI for two-file comparison
or trusted Git mode for MCP. This applies in both persistent-registry and
explicitly trusted MCP hosts. Git-mode requests resolve an absolute directory
(the process directory only when omitted) and verify it against the current
repository trust scope before creating a Git client. A host started for one
repository cannot redirect Git comparison to another; parse-only Git mode does
not register MSBuild.

The MCP tool description and `files` schema description state the limitation.
The local CLI and shared operation retain their two-file behavior. A future MCP
file mode needs an explicit verified root and contained reads of both declared
files, including symlink, mount, identity, and hard-link policy, before it can
be advertised as runnable.

## Consequences

- Production-path ToolRegistry tests prove file mode returns `Unsupported`
  without direct file reads or MSBuild registration, including relative and
  conflicting requests, while Git mode returns `TrustRequired` before reading
  an untrusted target.
- A trusted-host test proves startup trust does not authorize unrelated file
  paths or an unrelated Git directory. No direct host-file read is reachable
  through MCP file mode.
- The current command-wide catalogue still summarizes Git-mode prerequisites.
  Variant-specific product metadata and a safe file-root request remain RT-016
  work; callers receive an explicit mode result in the meantime.

## References

- [Architecture overview](../architecture-overview.md)
- [Pre-freeze surface audit](../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md)
