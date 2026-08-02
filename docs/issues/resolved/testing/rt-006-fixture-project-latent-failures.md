---
tags:
  - bug
  - tests
ticket: rt-006
severity: Medium
status: Resolved
area: testing
owner: ""
reported: 2026-07-24
summary: "RoslynTools.Tests has 12 latent failures (CI-confirmed) unmasked when the win-x64 RID was removed — the suite could not run before, so these never surfaced. Most are tests that launch a Debug-built rt.dll while CI builds Release."
---

# RoslynTools.Tests: 12 latent failures now visible

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
change took the fixture project from aborting (0 tests run) to a suite that
runs. The failures are pre-existing — the abort was hiding them, not preventing
them. They are not regressions from the RID removal.

**Counts differ by environment, and CI is the authority.** The ARM64 Windows
dev box first used to measure showed 34 failures; the Ubuntu CI runner (run
30065272023, the first green-able run after the RID removal) shows **12**. The
~22 difference was machine-specific to the ARM64 box — exactly the risk this
issue was opened flagging. Work the **12 CI failures**; ignore the ARM64-only
ones.

## The 12 (Ubuntu CI), by class

| Class | Count | Cause |
|---|---|---|
| `Infrastructure.CliBootstrap` | 5 | Launches `src/RoslynTools.Cli/bin/**Debug**/…/rt.dll`. CI builds **Release** with `--no-build`, so no Debug `rt.dll` exists on the runner and the process fails to start. (`CliBootstrap.cs` additionally hardcodes the TFM as `net8.0` while the project is `net10.0` — wrong on two axes.) |
| `Infrastructure.JsonEncoderUnification` | 3 | Same root cause — launches a Debug-built `rt.dll` that a Release CI build never produces. |
| `Refactor.Move` | 1 | Assertion failure. Not yet triaged. |
| `Mutation.ServiceHelpers` | 1 | Assertion failure. Not yet triaged. |
| `Architecture.ResultContractConstraints` | 1 | Assertion failure. Not yet triaged. |
| `AppRegistration` | 1 | Assertion failure. Not yet triaged. |

## Suggested order of attack

1. **The 8 `Infrastructure` failures share one root cause:** tests shell out to a
   Debug-built `rt.dll`, but CI builds Release. Make the harness resolve the
   `rt.dll` for the configuration and TFM it is actually running under (derive
   both from the running assembly, or from a shared test constant) rather than
   hardcoding `bin/Debug/net8.0`. One fix should clear all 8.
2. Triage the remaining 4 assertion failures individually against CI.

## Guardrail worth considering

A hardcoded build configuration or target-framework in a test path is exactly
the kind of mistake that recurs silently on the next TFM bump — or, as here,
the moment CI builds a different configuration than the test assumes. A small
architecture test asserting no test source hardcodes a `bin/Debug` or `netN.0`
path segment (they should derive both from the running assembly) would fail at
build time instead of only on the CI runner. Flagged, not built — see the
guardrails rule.

## Resolution (2026-08-02)

All 12 fixed, in two passes:

- **3 of the 4 untriaged assertion failures** (`AppRegistration`,
  `Mutation.ServiceHelpers`, `Architecture.ResultContractConstraints`) were
  fixed earlier — real bugs unrelated to each other, found while investigating
  what turned out to be pre-existing CI failures before the connection to this
  ticket was noticed: `InspectService` bypassed the injected
  `ISymbolResolutionService`, `ExtractClassService.BuildNewClassText` emitted
  block-scoped instead of file-scoped namespaces, and `DumpHelpCommand`
  bypassed the shared result renderer.
- **The 8 `Infrastructure` failures** (one root cause) and the **4th untriaged
  failure** (`Refactor.Move`) were fixed together: `CliBootstrap` and
  `JsonEncoderUnification` each hardcoded a `bin/Debug/...` path segment CI's
  Release build can't satisfy — consolidated into `RtCliHost`, which derives
  both configuration and TFM from the running test assembly's own path.
  `Move`'s failure was a second, unrelated bug: hardcoded Windows-style
  `C:\...` path literals in a test that pins `MoveService.cs`'s
  `GetRelativePath`/`Combine` pair, which Linux CI can't interpret as absolute
  paths — rebuilt with `Path.Combine`/`Path.GetTempPath()` to be portable.
- Added the guardrail this ticket flagged: `NoHardcodedBuildOutputPathInTests`
  fails the build if test source combines a `"bin"` literal with a literal
  `"Debug"`/`"Release"`/TFM segment in a `Path.Combine(...)` call. Verified it
  fails for the stated reason by reintroducing the exact mutant it guards
  against.
- Verified against `dotnet build -c Release` + `dotnet test -c Release
  --no-build` locally — CI's exact commands — not just local Debug.

## Decision log

- 2026-07-24 — Opened. The `fix/win-x64-fixture-rid` change fixed the 12
  `RoslynTools.Tests.Mcp` failures it targeted and unblocked the CI abort; the
  fixture-project failures it unmasked were explicitly left out of that change's
  scope and filed here for separate triage.
- 2026-07-24 — Corrected the count against CI. The ARM64 dev box showed 34; the
  first post-RID Ubuntu run (30065272023) shows **12**, of which 8 share the
  Debug-vs-Release `rt.dll` root cause. The other 22 were ARM64-specific and are
  out of scope — CI is the authority.
- 2026-08-02 — Resolved. All 12 fixed; guardrail built and hand-verified.
