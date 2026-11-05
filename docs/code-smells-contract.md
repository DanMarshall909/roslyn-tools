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
