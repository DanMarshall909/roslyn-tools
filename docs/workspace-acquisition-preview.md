# Workspace acquisition evidence: preview status

`WorkspaceCompleteness` now projects a declared input manifest and acquisition outcomes.
A successful compilation of the projects Roslyn happened to load is not proof that the
requested solution was fully acquired.

## What is implemented

- The loader declares `.sln`/`.slnx` project paths and fingerprints the selected solution
  before opening the workspace. Missing requested projects remain visible.
- Required context, reference, document, build-input, SDK, evaluation, generator, analyzer,
  snapshot, and manifest-closure stages remain explicit until their outcomes are known.
- Immutable receipts bind evidence to the exact Roslyn solution snapshot and source
  fingerprint. Duplicate, undeclared, missing, or mismatching outcomes fail closed.
- Closed manifests are cross-checked against observed contexts, documents, and references;
  required stages need affirmative completion or an explicitly valid exclusion. Fixture
  context identities are snapshot-local Roslyn IDs, not verified production TFM identities.
- Workspace diagnostics are collected before loading and through deferred acquisition.
  Severity and completeness impact are separate; unknown warning impact degrades the
  assessment without relying on English message fragments.
- The central policy distinguishes `Failed` (minimum usable scope unavailable), `Partial`
  (usable scope with unsatisfied evidence), and `Complete` (closed, satisfied evidence).
  Trust and evaluator provenance do not imply completeness.

## Deliberate remaining restriction

The production MSBuild workspace adapter does **not** yet provide authoritative closure
of evaluated target-framework/configuration contexts, imports, SDK/package resolution,
design-time compiler inputs, or every required generator/analyzer stage. These entries
are `Unknown`; a clean production compilation therefore remains `Partial` in this preview.
Arbitrary `AdhocWorkspace` instances are also unverified. Deterministic tests may attach
explicit closed-world receipts to exercise operations that require complete evidence;
those fixtures do not prove production acquisition is complete.

Consequently `delete-unreferenced` and `move-slice` refuse unverified production workspaces,
including ones with no compiler errors. Inspection remains available, but unused findings
are advisory and cannot carry deletion follow-ons. Completing the production collector
and proving a real complete solution are still required to close #122. There is no request
flag to self-certify completeness. Existing CI checks retain their own compilation/rule
checks; this change does not make CI depend on a local trust registry or interactive input.

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

## Remaining implementation slices

1. Add a caller-owned typed acquisition session to `IWorkspaceSessionFactory` and migrate
   the proof-sensitive operations. Preserve final assessment after semantic analysis so
   late diagnostics can downgrade the result; do not make the daemon an evidence owner.
2. Own the evaluated MSBuild/design-time input account: selected solution configurations,
   evaluated TFM contexts, imports, SDK identity, executed compiler arguments, sources,
   references, additional files, and analyzer configs. Start with a real single-context
   fixture whose executed analyzer/generator inventory is demonstrably empty.
3. Bind affirmative generator/analyzer execution receipts to the compilation actually
   consumed by analysis. [Roslyn 5.3 workspace APIs](https://github.com/dotnet/roslyn/blob/16f9bd284cd49604ac82998bfe778a8eb16d4347/src/Workspaces/Core/Portable/Workspace/Solution/Project.cs)
   expose generated documents, but not a
   public receipt for every workspace-owned generator run. Running a second driver over
   an already-generated compilation is not equivalent evidence.
4. Prove graph/context coverage and input stability, including changing file membership,
   project/import/source/reference contents, omitted TFMs, and stale mutation plans.
   Unknown build extensions or unaccounted external inputs must remain `Partial`.

These are follow-up requirements, not capabilities of the preview collector.
