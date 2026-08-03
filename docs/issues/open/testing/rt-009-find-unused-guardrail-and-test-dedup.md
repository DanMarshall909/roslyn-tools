---
tags:
  - testing
  - guardrail
  - find-unused
ticket: rt-009
severity: Medium
status: Open
area: testing
owner: ""
reported: 2026-07-31
summary: "Add a guardrail test pinning that find_unused never flags a wired-up *Command.Build, and de-duplicate the repeated MCP handler error-path test setup"
---

# Guardrail test for find_unused false positives + MCP test de-duplication

## Context

Two test-layer items surfaced while running the analysis suite (see rt-008 for
the underlying analyzer bug).

**1. Guardrail (primary).** `find_unused` currently reports every `*Command.Build`
as high-confidence dead code because it does not follow method-group-to-delegate
references (rt-008). This is exactly the [[guardrails]] shape: a repeatable,
high-blast-radius mistake a build-time test can mechanically prevent. The
guardrail must be written against the *rule* ("no wired-up command entry point is
ever reported unused"), sweeping every command, not against the one command that
happened to break today.

The test must be able to fail for the stated reason: it should assert on the
*contents* of the finding set (no `*Command.Build` present), not merely that the
command exits 0. Landed against today's analyzer it goes **red**; after rt-008 it
goes green — that red run is the evidence the guard can fail.

**2. Duplicate test setup.** `find_duplicates` flags a 4× structural clone: the
`using var provider = McpTestHost.CreateProvider();` error-path block repeated in
`DescribeHandlerTests`, `FindReferencesHandlerTests`, and `RenameSymbolHandlerTests`
(two occurrences). All five extractability checks pass. Extract a shared helper so
the missing-symbol / missing-argument error path is asserted in one place.

## Acceptance Criteria

- [ ] A guardrail test enumerates all command descriptors and asserts none of
      their `Build` entry points appears in `find_unused`'s output.
- [ ] The test asserts finding *contents*, not just exit code, and names the rule
      it enforces in its failure message.
- [ ] The test is wired into the CI/test project so it actually runs (not just
      present locally).
- [ ] The repeated MCP handler error-path setup is extracted into one shared
      helper; the three test files call it.
- [ ] After rt-008 lands, the guardrail is green; confirm it was red beforehand.

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-07-31 | Guardrail asserts on finding contents, not exit code | A test that only checks the exit code passes even when the analyzer starts reporting the wrong thing — see [[guardrails]] "its own test asserts only the exit code". |
