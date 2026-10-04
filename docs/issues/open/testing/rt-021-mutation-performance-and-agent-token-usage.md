---
tags:
  - testing
  - mutation
  - developer-experience
ticket: rt-021
area: testing
reported: 2026-10-04
github: https://github.com/DanMarshall909/roslyn-tools/issues/149
summary: "Reduce mutation compute and agent supervision costs while preserving behavioural evidence"
---

# Mutation performance and agent token usage

[GitHub issue #149](https://github.com/DanMarshall909/roslyn-tools/issues/149)
owns priority, workflow status and acceptance criteria. This file retains the
investigation and decisions; its open location means the investigation is not
archived.

## Priority decision

On 2026-10-04, the user required this to be the next RT task before other
product-spine or Flo feature work. Useful long runtimes are acceptable; high
agent token use is the more pressing concern. Preserve required mutation scope
and behavioural evidence while reducing repeated work and supervision.

The preceding discovery implementation is preserved in checkpoint commit
`42d03a47c73b0406bc48adf78b709bce70cf6b44`. Its Release tests and formatting
passed against the retained source candidate. Complete Core mutation evidence
and final independent review remain outstanding; this checkpoint does not close
the relevance acceptance task or establish preview readiness.

## Retained measurements

RT pins Stryker.NET 4.14.1. The completion configurations selected 4,436 tests per
target with approximately 20 minutes for initial tests and coverage capture.

| Target | Configured files | Scheduled mutants | Elapsed | Outcome |
| --- | ---: | ---: | --- | --- |
| CLI | 22 | 1,138 | 3h 46m | Terminal exit 0; findings retained |
| Hosting | 2 | 71 | 2h 30m | Terminal exit 0; findings retained |
| Core | 62 | 10,003 | 15h 20m | User interruption, exit 130; no machine report |

Scheduled counts are not completed counts. The Core run and its owned workers
stopped without altering the source candidate. A native exit 0 does not itself
establish mutation adequacy.

A separate retained Hosting diagnostic run assigned all selected tests to 70
created metadata mutations. Thirty-five start-to-next-start intervals had a
median of 494.47 seconds, including runner overhead. This supports investigating
static-initializer fallback. It does not establish each latest Hosting mutant's
execution selection. Early bail was already observed working.

The broad goal counter recorded 38.2 million tokens without activity attribution.
It cannot establish the mutation share or monetary cost. Local Stryker execution
uses compute; agent polling, reasoning, report inspection and repeated unchanged
checks consume model tokens.

## Investigation direction

Start with independent whole-owner metadata contracts and a calibrated small
test profile. Keep full regression and separate E2E verification. Compare
representative timings and test/mutant participation before adopting the profile.
Then calibrate the pinned tool's persistent result reuse, including test-only
changes and invalidation of incompatible state. Prefer Stryker's own scheduling.

Use an unattended driver and compact linked findings. Record phase timings,
actual progress when available, resource observations and attributable agent
cost. Preserve cancellation evidence and disclose unsupported progress or resume
capabilities. Increasing concurrency follows measurement of the main bottleneck.

## Evidence locations

- Retained completion configurations, reports, source bindings and local gate:
  `/tmp/rt-relevance/final-v4/`.
- Core cancellation diagnostics:
  `/tmp/rt-relevance/final-v4/core-stop-diagnostics/`.
- Earlier bounded Hosting analysis:
  `/tmp/rt-gate-efficiency-analysis/findings.md`.
- Shared guidance update: `DanMarshall909/ai-rules@802c580`.

The local evidence paths are machine-local; the measurements and limitations
above remain available in this versioned record and the linked issue.
