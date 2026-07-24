---
tags:
  - bug
  - tests
ticket: rt-006
severity: Medium
status: Open
area: testing
owner: ""
reported: 2026-07-24
summary: "RoslynTools.Tests has 34 latent failures unmasked when the win-x64 RID was removed — the suite could not run before, so these never surfaced"
---

# RoslynTools.Tests: 34 latent failures now visible

## Context

The fixture project `tests/RoslynTools.Tests` carried a bare
`<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, which forced a win-x64 test
host. That host does not exist on the Ubuntu CI runner or on a non-x64 dev box,
so the run **aborted before any test executed** (`Could not find 'dotnet.exe'
host for the 'X64' architecture`). It only ran on the x64 Windows machine it was
authored on.

The RID was removed to unblock CI and to fix the sibling
`RoslynTools.Tests.Mcp` project, whose portable host could not resolve the
RID-specific fixture assemblies (see the `fix/win-x64-fixture-rid` commit). That
change took the fixture project from aborting (0 tests run) to **1541 passed /
34 failed / 9 skipped**. The 34 are pre-existing — the abort was hiding them,
not preventing them. They are not regressions from the RID removal.

## The 34, by area

| Class prefix | Count | Observed cause |
|---|---|---|
| `Infrastructure` | 23 | CLI-bootstrap harness. At least 4 fail with "Could not execute because the specified command or file was not found" — `CliBootstrap.cs` hardcodes `src/RoslynTools.Cli/bin/Debug/net8.0/rt.dll`, but the project targets `net10.0`, so the path does not exist. (`JsonEncoderUnification.cs` already uses `net10.0`.) The stale `net8.0` path is the same "cleanup left it behind" family as the RID itself. |
| `Navigation` | 3 | Assertion failures (`Assert.Contains`, Shouldly). Not yet triaged. |
| `Architecture` | 1 | Assertion failure. Not yet triaged. |
| `Mutation` | 1 | Assertion failure. Not yet triaged. |
| `AppRegistration` | 1 | Assertion failure. Not yet triaged. |
| other | 5 | Scattered assertion failures. Not yet triaged. |

## Caveat on reproduction

The failure counts above were measured on an ARM64 Windows dev box. Some of the
non-`Infrastructure` assertion failures may be environment-specific (path
casing, line endings, tool availability) rather than genuine logic bugs — each
needs to be reproduced on the Ubuntu CI runner before it is treated as real.
The stale-`net8.0`-path failures are environment-independent and will fail
everywhere.

## Suggested order of attack

1. Fix the hardcoded `net8.0` path in `CliBootstrap.cs` (resolve the framework
   from the running assembly, or read it from a shared test constant rather than
   hardcoding a TFM). Re-measure — this likely clears most of the 23
   `Infrastructure` failures at once.
2. Re-run on CI (Ubuntu) to separate genuine logic failures from
   this-machine-only ones.
3. Triage the remainder individually.

## Guardrail worth considering

A hardcoded target-framework moniker in a test path is exactly the kind of
mistake that recurs silently on the next TFM bump. A small architecture test
asserting no test source hardcodes a `netN.0` path segment (they should derive
it) would fail at build time instead of at the next framework upgrade. Flagged,
not built — see the guardrails rule.

## Decision log

- 2026-07-24 — Opened. The `fix/win-x64-fixture-rid` change fixed the 12
  `RoslynTools.Tests.Mcp` failures it targeted and unblocked the CI abort; these
  34 fixture-project failures were explicitly left out of that change's scope
  and filed here for separate triage.
