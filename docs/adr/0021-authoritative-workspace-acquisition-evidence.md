# ADR-0021: Authoritative workspace acquisition evidence

- Status: Accepted
- Date: 2026-09-23
- Related: [#122](https://github.com/DanMarshall909/roslyn-tools/issues/122)

## Context

Roslyn's loaded `Solution` is an observed design-time result, not an authoritative declaration
of everything that should have participated in an operation. A project can be omitted, one
target-framework context can fail, an import or reference can be unavailable, or a generator
can fail while the projects that did load still compile. Treating that snapshot as complete can
turn reference absence into unsafe deletion advice.

The existing completeness contract already defined manifests, per-input outcomes, typed
diagnostics, and `Complete`, `Partial`, and `Failed` states. Production acquisition could not
close the manifest because it derived evidence only from the graph that Roslyn successfully
returned. It therefore could not distinguish a genuinely complete workspace from a clean but
silently reduced one.

## Decision

Production MSBuild acquisition declares its expected semantic inputs before opening the Roslyn
workspace. An in-process collector independently evaluates every solution project and each
declared target-framework context, recursively including evaluated project references that are
not listed directly in the solution. It records projects, documents, project, assembly, and
analyzer references, SDK identity, evaluated imports, configuration, and physical input
fingerprints. Package declarations require a matching restore target plus existing target assets;
compile assets are reconciled with Roslyn metadata references. Framework declarations must match
the SDK's known framework-reference inventory or restore evidence.

A whole-input fingerprint brackets manifest discovery, and a project-aware fingerprint brackets
Roslyn loading. Stable before/after membership, reusable input discovery, and agreement between
physical documents and the loaded solution are mandatory inputs themselves. New default-glob
files, build props/targets, references, restore assets, or other relevant membership during either
window therefore prevent `Complete` even when the surviving compilation has no errors.

After Roslyn loading, the collector reconciles that declaration with the exact design-time
snapshot. It records loaded compilation contexts, design-time documents and metadata
references, executes source generators to observe generated documents, verifies analyzer
loadability, fingerprints newly observed physical inputs, and binds the receipt to the Roslyn
solution revision. Collector failures, policy blocks, and unknown evidence cannot be upgraded
merely because a corresponding Roslyn object loaded.

The acquisition registry permits one fenced declaration refinement before receipt attachment.
The refinement must target the same workspace and current solution snapshot, and no receipt may
already exist. A receipt remains immutable after attachment.

Workspace loading returns typed failed or partial acquisition evidence when Roslyn cannot open
the requested solution but a workspace exists to carry that evidence. The canonical
`WorkspaceLoadResult` and read-only lease carry the immutable assessment at the application
boundary, and semantic operations reassess after deferred work. Cancellation still propagates.
Shared result projection preserves the manifest, diagnostics, evaluator identity,
trust state, configuration, frameworks, SDK/compiler identity, and source revision identically
for CLI and MCP hosts.

Diagnostic analyzer execution is excluded for current semantic operations after analyzer
assemblies are proven loadable because those operations do not consume analyzer diagnostics.
That exclusion is explicit and validated in the manifest. An operation that depends on analyzer
output must declare and execute that stage before it can receive complete evidence.

## Consequences

- Production MSBuild workspaces can be `Complete` when evaluated declarations and the Roslyn
  snapshot agree; missing projects, contexts, inputs, and required stages remain visible.
- Proof-sensitive policy can rely on affirmative evidence rather than successful compilation of
  the surviving graph. One shared proof policy blocks absence claims, deletion advice, and
  automatic mutation suggestions for partial and failed workspaces.
- Project evaluation occurs once to discover the expected graph and again through Roslyn's
  design-time build. Physical hashing and full structured provenance also increase acquisition
  cost and result size.
- The core library has a direct compile-time dependency on Microsoft.Build evaluation APIs; the
  host still selects and registers the installed SDK before acquisition.
- Arbitrary `AdhocWorkspace` instances stay unverified unless a trusted test or host attaches a
  valid closed receipt.
- `Complete` describes the declared local semantic workspace. It does not prove that reflection,
  framework activation, external consumers, or other runtime edges are absent.

## Alternatives considered

- **Infer completeness from `Solution.Projects`.** Rejected because omitted projects and failed
  target-framework contexts are absent from the evidence being used to detect them.
- **Treat a clean compilation as complete.** Rejected because it proves only that the surviving
  compilation graph was usable.
- **Require daemon-owned workspace authority.** Rejected because acquisition evidence belongs to
  the canonical workspace operation; the daemon remains a transport and bounded cache adapter.
- **Run all diagnostic analyzers for every semantic operation.** Rejected because their
  diagnostics do not affect the current operations' semantic inputs and would add unrelated cost
  and side effects. Operations that consume them must opt in explicitly.

## References

- Related ADRs: [ADR-0011](0011-authoritative-trust-and-evaluator-isolation.md),
  [ADR-0017](0017-bounded-read-only-workspace-reuse.md),
  [ADR-0019](0019-parse-integrity-gates-absence-claims.md)
- Related docs: [architecture overview](../architecture-overview.md),
  [workspace acquisition evidence](../workspace-acquisition-preview.md),
  [unused-symbol liveness policy](../find-unused-liveness.md)
- Specification: [critical platform hardening](../../openspec/changes/critical-platform-hardening/specs/platform-hardening/spec.md#requirement-workspace-completeness-governs-confidence-and-advice)
