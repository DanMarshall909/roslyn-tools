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

On 2026-10-06, the user made applying verified transferable discoveries to the
authored global rules and affected skills part of this goal's completion scope.

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

## Metadata profile measurements, 2026-10-06

The new `stryker-hosting-config.json` retains both complete owner files,
`HostCommandMetadata.cs` and `Output/RtContext.cs`, both test projects, Release,
four workers and the historical 100/95/0 thresholds. Its positive filter selects
48 independent host contract cases, five output-layer cases and two in-process
host-boundary cases: 55 tests. Full regression and E2E verification remain
separate requirements. This profile does not complete discovery admission.

All 467 production/build-input entries and 306 pre-existing test-input entries
in the retained source binding matched before the pilot. The new assertions
specify command identity, placement, formats, aliases, examples, discovery
triggers, effect/authority/workspace policies, resources and disclosed limits.
Fresh isolated assembly loading observes output defaults without a fixture
reset and checks independent changes and restoration of all six properties.

| Run | Selected tests | Eligible mutants | Killed / survived | Total seconds | Sampled process-group peak GiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Retained baseline | 4,436 | 71 | 33 / 38 | 8,980.478 | Unavailable |
| Initial compact pilot | 55 | 71 | 68 / 3 | 173.314 | 1.82 |
| Final profile, first run | 55 | 71 | 70 / 1 | 98.338 | 1.83 |
| Final profile, repeat | 55 | 71 | 70 / 1 | 185.990 | 1.86 |

The two final runs are 48–91 times faster than the retained baseline. Their
98–186 second range is material variability; its cause has not been isolated.
These are same-machine comparisons with unchanged production and existing
tests, rather than randomized measurements controlling all machine activity.
All 71 eligible mutations matched by file, location, operator and replacement;
every previously killed mutation remained killed. Authored input hashes stayed
unchanged during each run and its owned process group was empty at completion.

| Log milestone window | Baseline seconds | Final first / repeat seconds |
| --- | ---: | ---: |
| Startup and build | 31.725 | 21 / 20 |
| Initial tests and mutation preparation | 576 | 15 / 20 |
| Coverage capture and filtering | 543 | 8 / 8 |
| Mutant execution and reporting | 7,829.753 | 54.338 / 137.990 |

These are native log milestone windows, including the named overhead. Exact
isolated initial-test time is unavailable at the retained info verbosity.
No result reuse or changed-mutant subset was enabled for these pilots.

### Fault calibration and remaining finding

Native metadata faults resolve to named independent contract tests, including
Config read effects/conditional mutation, Trust write effects/conditional
mutation and Feedback mutation/JSON classification. RtContext has no generated
native mutants in this pinned run, so its correctness is not inferred from the
native score.

An isolated clean-commit experiment rejected sixteen explicit source faults:
six output defaults, a no-op setter, a coupled setter, Config/Trust authority
and workspace requirements, the three explicit format lists and Trust placement.
Every rejection was a named TRX assertion failure; setup failures cannot qualify.
Both calibration groups passed all 48 cases before and after source restoration,
and the experiment worktree returned clean.

The current-source Release gate passed in 763.894 seconds: build, 308 MCP tests,
4,404 main-project tests, nine pre-existing skips and whole-solution formatting.
The 48 contract cases also passed with coverage collection: metadata 117/117
lines and output context 6/6 lines. Coverage is execution evidence; the native
and explicit source-fault rejections establish the assertions' effectiveness.

The sole remaining native finding, ID 745, deletes the generic comparison and
recommendation connective in SemanticDiff's prose. Independently asserted Git
and file comparison modes and comment/whitespace behavior remain disclosed.
The profile does not pin that wording verbatim solely to kill this mutation.
The final native score is 98.59%; native success remains distinct from full
repository and discovery acceptance.

### Supervision, guidance and workflow findings

The third pilot recorded three launch/collection tool calls and 465 bytes of
serialized tool-response payload. Local sampling and waiting require no model
calls. These figures exclude implementation, review and other inspection calls;
they are not token counts. Mutation-attributed tokens remain unavailable.

Transferable findings are published in shared AI rules:

- `DanMarshall909/ai-rules@8ea321a`: compact independent contracts for static
  metadata, real initialization checks and configured owner-scope comparison.
- `DanMarshall909/ai-rules@ce0bec6`: identify entrypoint file types before text
  inspection, bound composed tool output and use a script's declared interpreter.

The first update passed ten repository checks; the second passed generator,
convention, link and whitespace checks. Installed portable and Claude skill
links resolve to the authored checkout; running-client reload is not proven.

Flo's documented implementation cycle cannot express honest characterization
RED for already-correct behavior. [FLO-036 / issue #1](https://github.com/DanMarshall909/flo-tdd/issues/1)
records the observed usage/documentation gap and this explicit calibration
fallback; no live-cycle rejection is claimed. RT's advertised Linux gate script
is tracked as non-executable (100644); its initial launch failed with errno 13.
The same full gate is invoked through Bash, retaining the failed launch record.

Raw reports, source/tool/configuration bindings, sampled process identities,
calibration TRX files, complete owned findings and compact summaries remain under
ignored `artifacts/mutation-performance/rt-021/`. The scripts there are bounded
investigation utilities. At this first-slice checkpoint, persistent reuse,
cancellation calibration, the maintained driver and final discovery review were
outstanding; the supervisor slice below records the subsequent progress.

## Native reuse calibration and maintained supervisor

The installed 4.14.1 NuGet manifest identifies producer source commit
`29cf66d848ae6d0d9272f86348e3a4eb57008158`. Primary producer files and raw
reports are retained under the investigation artifacts. Native probes found:

| Probe | Seconds | Native exit | Eligible outcomes |
| --- | ---: | ---: | --- |
| Missing baseline | 40.397 | 0 | All 71 ignored; no established score |
| Standalone full baseline reporter | 169.096 | 1 | 70 killed / 1 survived, then reporter failure |
| Same-input verified report preload | 47.848 | 0 | All 71 ignored despite a load message |

The preload used the successful third whole-owner pilot after verifying every
recorded input hash. It was a controlled provider probe, not an accepted native
seed. Source/test change probes were deferred after unchanged reuse failed.
The experimental worktree's provider reported branch `main` although its actual
branch was `experiment/rt021-context-calibration`. The unchanged primary probe
left one owned worker; PID/start-time verification preceded SIGINT, and the
group was confirmed stopped. These findings rule out adopting native reuse here.

`scripts/mutation.mjs` is the maintained alternative, discoverable through
`npm run mutation`. It preserves whole-config native execution and thresholds,
then reuses only identical verified state. Changed source, tests/shared fixtures,
configuration, tool/SDK/dependencies or execution environment require an explicit
full seed. Repository, checkout, branch and target namespaces remain separate.
The durable atomic bundle contains the exact report, full verification and
findings outside disposable build output. Native partial/incompatible reports
never become reusable results. No subset is promoted to a whole score.

Nine integration tests cover missing/corrupt/unreachable state, meaningful input
invalidation, full versus seed/reset semantics, artifact cleanup, source-bound
killer attribution, cancellation and lingering owned workers. Initial named RED
failures were behavioral (`unsupported_operation` versus missing-state recovery,
and exit 2 versus a successful full seed), rather than compiler/setup failures.
The existing Flo workflow does not document Node verification commands; this
additional friction is recorded on [FLO-036](https://github.com/DanMarshall909/flo-tdd/issues/1#issuecomment-6015325316)
before using the repository's real Node harness as a manual fallback.
Four separate named guard faults also fail the owning integration tests:
compatibility bypass, ignored native failure, ignored mid-run input change and
missing named killer validation. Each calibration starts at a clean frozen
commit; source restoration and final clean status are recorded.

Final independent review found that cached findings were not checked against the
source-bound report. A named corruption test failed before the repair; deleted
or tampered findings now reject reuse. The final nine-case GREEN run also
captured subprocess V8 coverage: 19,633 of 22,851 UTF-16 source units executed
across 47 driver captures. This diagnostic identifies remaining error branches,
CLI input checks and forced signal escalation; it does not establish assertion
strength or other-platform support. Raw ranges and their disposition remain in
the review packet.

The actual Hosting supervisor run completed in **178.312 seconds**, retaining
all **71** terminal outcomes (**70 killed / 1 survived**) and all **55** named,
source-bound test identities. Relevant inputs and HEAD remained unchanged,
diagnostic streams drained and the owned process group ended empty. Sampled
peak group RSS was **1,888,116 KiB**. A subsequent real fast invocation preserved
those outcomes while launching **no Stryker process**. Source/configuration/tool
scope remains the same as the admitted metadata profile; the prose deletion
remains an observable survivor.
These real producer measurements precede the final cached-findings repair and
retain their original wrapper hash. The repaired wrapper deliberately rejects
that older seed; final CLI seed/reuse tests cover the repair, while primary
namespace native seeding remains follow-up work.

Full execution emitted **852 stdout bytes** across its start and terminal JSON
events; cached reuse emitted **928 stdout bytes**. These are supervisor stdout
metrics, distinct from tool response envelopes, controller call counts and agent
tokens. Agent token attribution remains unavailable. Linux process ownership,
signals, disk observations and phase milestones are recorded in the receipts.
Elapsed time alone does not cancel a run; interrupted outcomes are incomplete.

Shared guidance now includes these outcome, namespace, environment, attribution,
durability and lifecycle lessons in ai-rules commit `befb088`. Incremental reuse,
other-platform cancellation and final Core/discovery admission/review remain
open. The existing frozen discovery gate is still authoritative.

## Primary calibration and configuration binding follow-up

The reviewed supervisor at `f56b258` completed a fresh primary-checkout Hosting
seed in 179.949 seconds, preserving 71 outcomes (70 killed / one survived).
Streams drained and no live owned worker remained; the receipt retained one
exited zombie with zero RSS. Sampled peak group RSS was 2,861,476 KiB. Full
stdout was 848 bytes; unchanged reuse was 926 bytes and launched no Stryker.
These are stdout metrics, not agent token attribution.

Actual production mutability, removal of an existing named test assertion,
configuration concurrency and execution-environment changes each rejected reuse.
Captured source bytes were restored, and HEAD/full status matched the clean
anchor before each change and after restoration. Restored reuse retained 70/71.
Evidence is retained in `artifacts/mutation-performance/rt-021/primary-driver-calibration/`.
RT outlines returned the exact target types/members with Partial workspace
evidence caused by ignored artifact build inputs; no full-workspace claim is made.

Preparing the preserved Core gate exposed another configuration boundary: an
explicitly selected configuration beneath ignored artifacts was absent from Git
inventory and therefore absent from the fingerprint. A genuine named RED showed
that changed owner scope incorrectly reused the old result. The selected path is
now always inventoried explicitly; changed ignored configuration rejects reuse,
and restoration permits it. All ten driver integration cases pass. This wrapper
change deliberately invalidates earlier seeds; no old result is migrated by
assumption. The existing Core scope, test filter and thresholds remain unchanged.
The transferable explicit-input inventory lesson is published in global AI rules
commit `8849c12`, with generation, convention and whitespace checks passing.
