# Extensible code smell review

Issue: https://github.com/DanMarshall909/roslyn-tools/issues/158

Dan authorized a separate extensible `code-smells.json`, containing all rule and
response information needed by MCP, and its integration into MCP and Jev.
This document defines the accepted implementation boundary; the runtime and
verification are not yet complete.

## User-facing contract

- Add `rt analyze code-smells` and the corresponding generic MCP `code_smells`
  tool through one typed application port. Preserve `solid-review` and
  `list-smells` compatibility. No bespoke MCP handler.
- Requests accept solution, optional catalogue path (`--config` / `config`),
  optional rule ID selection, provider/model, max candidates (default 20, 1–100),
  dry run and explicit transmit source. Preview is the default. Configuration
  cannot grant trust, trigger transmission, choose an endpoint or contain keys.
- Resolve explicit config relative to the selected repository, otherwise use
  `code-smells.json` at the selected repository root, falling back to a bundled
  default. Do not resolve a different catalogue from the process working
  directory or require a source checkout when running a packaged CLI/MCP host.
- Read once, size-bound and strictly validate the catalogue before workspace
  acquisition/provider calls: version, unknown members, duplicate IDs/JSON keys,
  missing/null/blank required fields, finite bounded policy values, supported
  selector/threshold keys and nonnegative integer thresholds. Actionable typed
  errors name the file/rule. Unsupported rules must never silently disappear.
- Return the effective catalogue/rules, response schema, content hash and version
  alongside every successful preview/live result. The checked-in response schema
  is a real validated contract; keep it aligned with typed results and prevent
  a custom file from claiming an incompatible runtime result shape.
- Eight starter rules use existing size and SOLID detectors, with thresholds in
  this file. Appending a new ID with an existing selector and a custom question,
  exceptions, severity and remediation must work without a code change.
  Support `semantic` detection with `class` or `method` selectors for genuinely
  custom questions over bounded declarations. This is semantic review, not an
  executable arbitrary detector language. New Roslyn algorithms still need code.
- Rule definitions include stable ID/name/category/enabled/severity/description,
  detector selector and thresholds, review question/evidence guidance/exception
  IDs and descriptions, and remediation. Evidence guidance is guidance, not a
  claim that source alone proves a cross-symbol architectural fact.
- Prepare candidates in deterministic rule/path/symbol order, cap globally,
  preserve raw findings and every skipped/beyond-limit reason. Sanitize source,
  bound source/evidence to the existing 16 KiB limits, respect ignored/generated
  scope and workspace completeness. Never present incomplete evidence as a
  confident violation. Cancellation propagates. Dispose leases.
- Live requests use the existing TypeSafe HTTP transport and environment key.
  Pass the effective rule question/description/exception choices into the
  existing provider request; use the same exception IDs to validate its reply.
  No schema or configuration content may override transport/security controls.
- Preserve all provider signals: original classification, probabilities,
  confidence, meaningful-design-problem and accepted-exception probabilities,
  selected exception and its probabilities/confidence. Apply the configured
  conservative policy separately and return policy reasons, location, stable
  candidate/rule IDs, raw evidence, remediation and provenance (requested/resolved
  model, request ID, revision, rule/question-set version and evidence hash).
  Summaries/remediation are catalogue/policy data, not invented Jev explanations.
- Preview returns `Planned`, zero assessments and estimated request count;
  live errors yield truthful Partial/Failed results retaining raw/plan evidence.
  CLI exits nonzero on failed/partial live execution. The tool requires explicit
  repository trust and declares external effects like `solid_review`.

## Verification boundary

One vertical Flo criterion: editing/appending a catalogue rule changes a bounded
review's provider question and allowed exceptions, and the shared port returns
the complete effective rule/response contract and honest advisory result.
Use existing generic port discovery/invocation to write compilable RED tests
before the new operation exists; do not add production behavior in RED.
Use an in-memory Roslyn workspace and recording provider/HTTP handler for fast
behavior tests. Cover preview zero calls, custom rule propagation and response
validation, deterministic bounds, invalid config before external work, policy
uncertainty, disabled rules, missing config fallback, provider failures and
source sanitation. Test actual CLI/MCP discovery, binding, trust and parity at
the appropriate host layer. Validate a serialized preview and live result against
the declared schema with an independent oracle. Include one bounded live
synthetic calibration after ordinary verification. No bulk live evaluation.

Preserve native Flo RED and later phases. Final local Release build, formatting,
fast/slow gates and independent exact-candidate review precede publication.
Mutation is a separate final-snapshot post-session gate and must be reported
honestly if pending. Existing worktrees are parked under the decision in #158.

## Retained first-cycle evidence and required corrections

The first native Flo run `20261105034211587-4afcc56f` is retained unchanged in
`/home/dan/code/roslyn-tools-worktrees/code-smells-config/.flo/worktrees/current-cycle`.
It has genuine reviewed RED, passing GREEN and a coverage collection. Refactor
could not launch: the pinned Flo runtime passes a >128 KiB prompt in one Linux
argument. Flo repair is separately owned; do not rewrite this cycle or its
pinned runtime. Coverage collection is not adequate coverage: follow-up tests
and final whole-ticket gates remain mandatory.

The copied candidate is an unreviewed local checkpoint, not finished behavior.
Before completion, a fresh Flo criterion must establish that every reviewed
candidate is the exact allowed declaration and every advertised result matches
the configuration and public contract:

- Apply `.rtignore` and generated-source exclusions consistently to SOLID as well
  as size and semantic candidates, before source preparation/transmission.
- Preserve exact syntax identity for methods, including nested types, overloads
  and two methods on one line; never reconstruct a method by line alone or
  substitute a containing type's source. Candidate IDs must distinguish them.
- Enforce the global preparation cap before sanitizing later declarations, not
  only before provider invocation. Keep raw/plan reasons without over-preparing.
- Require all selector threshold keys or materialize documented defaults into
  effective rules before hashing and returning them. Do not report a catalogue
  that differs from the thresholds actually used.
- Either apply configured classification descriptions in Jev requests or reject
  edits to invariant vocabulary. Validate policy threshold relationships.
- Match the file's camelCase response schema at real CLI and MCP serialization
  boundaries. A first real CLI preview returned PascalCase root fields and failed
  independent JSON Schema validation (19 errors); internal Web-default serializer
  tests missed that mismatch. Do not weaken the schema to accept both shapes.
- Preserve existing workspace completeness invariants. The new nullable lease
  accessor is not necessary justification for a new core contract; fix synthetic
  workspace fixtures to supply valid completeness evidence and use the established
  lease contract. Missing/partial acquisition must never appear as a complete
  negative result. Return actionable limitations/failed or partial outcomes.
- Strengthen the provider HTTP boundary for custom question/exception choices and
  stale response choices, fault-specific validation diagnostics, configured
  policy, semantic selectors, default fallback, and CLI/MCP binding/trust.
  Validate real preview/live serialized responses with independent JSON Schema
  tooling. Do not rely on an incomplete handwritten schema interpreter silently
  ignoring new schema keywords.

These are corrections within the original accepted behavior, not new features.
The new RED witness should fail for the ignored-source/candidate identity boundary;
additional cases preserve all required output and configuration invariants.
