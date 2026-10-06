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

## Core performance priority change (2026-10-07)

The 2026-10-06 Core run was cancelled through its verified Node supervisor
following a diagnostic window shorter than three minutes. Elapsed execution was
31,922.665 seconds (8h 52m), exit 130. Streams drained and all owned worker identities
exited. No machine mutation report exists: outcomes remain incomplete and no
resume claim is made. Raw diagnostics are retained under
`artifacts/mutation-performance/rt-021/core-stop-performance/`.

Measured phase boundaries: build 22s; initial testing and instrumentation 8m 54s;
coverage 8m 47s; remaining 8h 35m mutation execution. A supported 25-second managed
thread-time trace of one active test worker attributed 25.319s to Regex
construction within 25.602s sampled suggestion-test time (98.9%). This is a
single-worker sample, not whole-run CPU attribution. Existing unmutated TRX
contains 758 SuggestService cases totalling 195.536s and 176 relevance cases
totalling 49.362s. SuggestService contains 126 direct static regex operations across more
patterns than .NET 10's default 15-entry global cache. The trace exposes repeated
symbolic-regex construction rather than slow matching or blocked execution.

The smallest candidate change retains matching arguments/options and reuses only
regex instances for the finite service-owned pattern set. Queries, results and
assertions are not cached; active mutant pattern/options and ambient culture are
evaluated before lookup. Independent source review passed. RT inspection was
attempted first; outline does not support LLM output and its workspace acquisition
was partial or unstable during mutation. No absence-of-reference claim relies on
it. RT suggest found no supported local regex-cache refactoring; a bounded manual
preview/apply retained clean identical HEAD/status. Flo's existing characterization
workflow gap (FLO036) applies: these independently asserted contracts already
pass, so no fabricated RED cycle is claimed.

Static-initializer test expansion is supported by pinned Stryker 4.14.1 source.
The relevance baseline pilot identifies 51 static mutants, all killed; its
contribution to the interrupted full run remains unquantified. Workspace/build
setup contributes to baseline cost; the sample does not show it as the active
suggestion hot path. Timeout/restart contributions are unproved. 1,261 intake temp
directories were observed after shutdown; none has modification time inside the
run window, so their count does not establish a leak from this run. No directories
were deleted. RAM/disk snapshots showed headroom, not exhaustion.

Whole-owner pilots and frozen behavioral assertions precede another full run.
Thresholds, controls, survivors and final full qualification remain unchanged.
Shared lessons were published in ai-rules commit `d7f1462`; generated policy,
conventions and synchronization checks passed.

### Whole-owner comparison

Both before/after runs used Stryker.NET 4.14.1, SDK 10.0.301, Release,
concurrency 4, thresholds 100/95/0, unchanged owner source and unchanged selected
test catalogues. Native runs were sequential. Checkout/solution roots differed;
reports bind those paths explicitly. No baseline reuse or changed-mutant subset
was used. The invalid logging-config attempt ended before testing and is excluded.

| Whole owner | Tests | Scheduled | Before | After | Total gain | Mutation-phase gain |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| SuggestRelevancePolicy | 176 | 158 | 684.594s | 209.676s | 3.27x | 4.51x |
| OperationRequestIdentity | 8 | 67 | 102.889s | 94.199s | 1.09x | 1.12x |

Relevance retains 157 killed, one survivor, eight ignored and four compile errors.
Identity retains 67 killed and 23 ignored. Every generated owner mutant identity,
owner source, selected test ID/name/location and status matched. The survivor
is `Append` to `Prepend` in `CapabilityTerms`; it remains visible and unclassified
by this performance slice. One baseline identity worker was recorded as a zombie
with zero RSS; no live owned workers remained. Outcomes are terminal for these
pilots, separate from the incomplete full Core run.

All 934 existing query cases passed. Summed duration was 244.898s in the retained
unmutated baseline TRX and 2.787s in the fresh candidate TRX (87.89x). This is
historical per-test duration evidence, not contemporaneous end-to-end timing.
Focused coverage executed all nine cache lines; existing SuggestService coverage
was 817/822 lines. No assertion or test file changed.

The 10x mutation target is not achieved. Relevance setup remains about 94s and
mutation execution 115s; the 51 static mutants remain static. Cold construction,
early activation and restart contributions need a separate measurement before
another production optimization. The other owner changed little, confirming
that the query optimization is local. A reliable whole-Core runtime estimate
cannot be derived from these 225 scheduled mutants or the interrupted full run;
no new full run was launched. Preserve the full
scope, controls and final qualification while obtaining representative static
and workspace costs. Raw artifacts remain under
`artifacts/mutation-performance/rt-021/core-performance-pilots/` in both checkouts.

### Static-input activation comparison

A second clean-commit refactoring keeps the static `Admit` entrypoint and its
algorithm in place, but constructs a private policy instance for each admission.
The same 51 filler words and ordinal comparisons are instance inputs. Generated
regex factories remain static. No query answer, test assertion, selected test,
threshold or mutation operator changed. The allocation of one small set per
admission is a deliberate trade-off measured against existing behavior.

Matched DEBUG-verbosity whole-owner runs used the same checkout, Native 4.14.1,
SDK 10.0.301, Release, concurrency 4 and all 176 relevance tests:

| Measurement | Before | After | Gain |
| --- | ---: | ---: | ---: |
| Complete supervised run | 188.183s | 172.619s | 1.09x |
| Scheduled mutation phase, including final drain | 97.667s | 89.499s | 1.09x |
| Static mutants | 51 | 0 | |
| Summed runner-session seconds | 370s | 349s | |

Runner debug messages pair 51 static sessions totalling 148 runner-seconds and
99 ordinary sessions totalling 222 seconds before the change; afterwards all
150 sessions are ordinary. These sums describe concurrent occupancy at one-second
log resolution, not CPU time or elapsed wall time. The modest observed difference
does not support attributing most remaining runtime to static activation. This
single pair is not a precise estimate of a 9% gain under variable system load.

All 158 scheduled outcomes remain 157 killed and one survivor; eight ignored
and four compile errors remain visible. Logical mapping retains all 51 former
static faults, each killed, and all other scheduled identities and statuses.
Mapping uses the owning member, operator, original UTF-16 source snippet and
replacement, preserving duplicate multiplicity/source order and normalizing only
the introduced `policy.` receiver. The ignored whole-`Admit` block-removal mutation
has changed source text because construction was added; its before/after records
are explicitly unmatched, not silently credited as identical. Numeric IDs and
shifted source coordinates are not used as identity proof. The complete selected
test catalogue is unchanged.

All 934 existing query cases passed. Focused coverage executes all 98 authored
policy lines across its generated closure/iterator classes, including the new
construction path. Raw reports, clean preview/apply checkpoints, logical mappings,
runner timings and verification logs are retained under
`artifacts/mutation-performance/rt-021/static-inputs/` in the candidate worktree.
RT/Flo's previously recorded refactoring/characterization gaps still apply; no
unsupported automated refactor or artificial RED qualification is claimed.

The DEBUG pilot exposed a supervisor metadata defect: its unanchored milestone
matcher captures instrumented source comments containing "coverage". Benchmark
phase extraction requires actual timestamped INFO scheduling messages. Fixing
that parser is a separate slice; raw native logs remain the authority. The 10x
mutation target and a defensible full-Core estimate remain unmet. No new full
Core run was launched.

The full Release local gate also passed: 4,404 CLI/Core and 308 MCP tests, with
the same nine skips and no format changes. Main-suite duration was 6m 10s.
Independent benchmark/source review confirmed the logical mapping and the modest
observed benefit. The next representative pilot must use the full Core test
selection with a whole owner to measure broad test reruns; the 176-test pilot
cannot establish that cost. Global measurement lessons are published and pushed
in ai-rules commit `6a04994`.

### Progress-metadata correction

The supervisor now accepts timestamped Native INFO records before extracting
milestones or the scheduled count; the count must occupy the whole message.
Instrumented source comments and DEBUG messages stay in the raw log. A real RED
failed because four such lines appeared in metadata; the focused GREEN passes.
The test uses observed Native 4.14.1 INFO formats and independently specifies the
expected records and raw-log retention. Review identified that a later valid count
masked false-count parsing; INFO examples with a prefix/suffix now follow the last
valid count. Reverting only the whole-message count rule produces a second real
RED; restoring it passes. These false INFO counts are also excluded from milestones. RT/Flo do not support this Node contract;
the scoped fallback uses the existing Node integration driver.

A post-hoc projection of the preserved DEBUG Native log selects four genuine
records instead of 52, with scheduled count 158 matching its machine report.
The source log is unchanged and SHA-bound in `native-log-calibration.json` under
`artifacts/mutation-performance/rt-021/milestone-parser/`. This projection is not
another mutation run. Changing the supervisor intentionally invalidates older
wrapper baselines; no previous baseline is silently migrated. This narrows
misleading receipt metadata while retaining raw diagnostics and all outcome,
threshold, cancellation, stream-drain and process-ownership controls.

All 11 supervisor integration cases passed, including exact-state reuse,
invalidation, corrupt reports, owned-worker cleanup and graceful cancellation.
Node syntax and whitespace checks passed. The preceding full Release gate covers
the unchanged C# code; it is not rerun for this Node-only metadata slice.
