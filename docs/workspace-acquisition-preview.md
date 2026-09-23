# Workspace acquisition evidence: preview status

`WorkspaceCompleteness` now projects a declared input manifest and acquisition outcomes.
A successful compilation of the projects Roslyn happened to load is not proof that the
requested solution was fully acquired.

## What is implemented

- The loader declares `.sln`/`.slnx` project paths and fingerprints the selected solution
  before opening the workspace. Missing requested projects remain visible.
- A production collector evaluates every declared project and target-framework context before
  Roslyn acquisition, recursively including evaluated project references outside the solution
  inventory. It records the configuration, SDK identity, resolved imports, project, assembly,
  and analyzer references, package/framework declarations, source/additional/analyzer-config
  files, and their physical fingerprints.
- Package evidence comes from the selected restore target and physical package assets, not the
  declaration alone. Required compile assets are reconciled with the loaded metadata graph;
  missing package DLLs, hint paths, restore graphs, or SDK framework identities fail closed.
- During design-time loading the collector reconciles that declaration with the exact Roslyn
  contexts, metadata references, generated compiler inputs, generated documents, and analyzer
  and generator inventory. Inputs discovered only by the design-time build are added before the
  immutable receipt is attached.
- Immutable receipts bind evidence to the exact Roslyn solution snapshot and source
  fingerprint. Duplicate, undeclared, missing, or mismatching outcomes fail closed.
- Whole-input fingerprints bracket both manifest discovery and Roslyn loading. Stable membership
  and snapshot agreement are mandatory, so new default-glob files or build configuration that
  appears in either window cannot inherit earlier `Complete` evidence.
- Closed manifests are cross-checked against observed contexts, documents, and references;
  required stages need affirmative completion or an explicitly valid exclusion. Physical
  source, import, analyzer, and metadata-reference changes invalidate the receipt.
- Workspace diagnostics are collected before loading and through deferred acquisition.
  Severity and completeness impact are separate; unknown warning impact degrades the
  assessment without relying on English message fragments.
- The central policy distinguishes `Failed` (minimum usable scope unavailable), `Partial`
  (usable scope with unsatisfied evidence), and `Complete` (closed, satisfied evidence).
  Trust and evaluator provenance do not imply completeness.

## Deliberate restrictions

Production MSBuild workspaces can now be `Complete` when the evaluator and Roslyn snapshot
agree and every required input is accounted for. Missing projects, failed project references,
unavailable target-framework contexts, unreadable imports, analyzer/generator load failures,
and post-load input changes produce `Partial` or `Failed` evidence.

Arbitrary `AdhocWorkspace` instances remain unverified. Deterministic tests may attach an
explicit closed-world receipt, but ordinary requests cannot self-certify completeness.
Diagnostic analyzer assemblies are loaded to validate their inventory; their rule diagnostics
are explicitly excluded because current semantic operations do not consume them. Any future
operation that depends on analyzer output must declare and execute that analyzer stage.

`Complete` is a statement about the declared local semantic workspace. It does not prove that
reflection, framework activation, external consumers, dynamically loaded assemblies, or other
runtime edges are absent. `find-unused` and `delete-unreferenced` retain those limitations even
for a complete workspace.

## `find-unused` output contract

JSON and MCP now return the same `rt.result.v1` envelope. Consumers of the old top-level
`symbols` array must read `data.symbols`. The envelope carries `workspace.state`, declared
and observed inputs, requested/failed project identities, compiler/evaluator provenance,
typed diagnostics, confidence limitations, and timing. `source.revision` identifies the
loaded Roslyn input snapshot; it is not a substitute for manifest closure.

Machine diagnostics retain all affected projects, including errors beyond the previous
20-diagnostic limit. Hiding console workspace warnings does not remove machine evidence.
Text, Markdown, and LLM formats retain their symbol presentation with visible workspace
state and limitations, including when there are no findings.

Unsupported syntax is not reinterpreted as valid method/type declarations: files with
syntax errors contribute no unused candidates. Valid neighboring files remain analyzable.
Known external dispatch and module initialization are governed independently by the
[conservative liveness policy](find-unused-liveness.md).

The daemon remains a transport and cache adapter. Acquisition evidence is owned by the
workspace load and is re-assessed after semantic work so late diagnostics can still downgrade
the result.
