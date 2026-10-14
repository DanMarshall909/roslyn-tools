# ADR 0106: Separate slow regression verification from mutation feedback

## Status

Accepted, 2026-10-14, for RT-021 and the user's explicit slow-lane instruction.

## Context

The read-only audit at `15fe44f` measured 127 suggestion CLI journey rows at
122.132 summed seconds and 221 journaled apply rows at 82.727 seconds. These
tests retain useful command, physical persistence, coordination and real process
boundaries. Running them repeatedly during implementation and mutation adds
cost without replacing the cheap owning-layer tests.

## Decision

Keep every test assertion and data row. Define the whole
`RoslynTools.Tests.Cli.SuggestWorkflowSurfaceTests` and
`RoslynTools.Tests.Infrastructure.JournaledApplyAuthorityTests` groups as slow in
`scripts/test-lanes.mjs`.

The local verification drivers default to fast tests. An explicit slow lane
runs its owning project; an all lane runs both kinds of tests within the selected
projects. `--full --lane all` is the unfiltered ticket-completion gate. Run the
slow lane once against the final ticket candidate, with an earlier focused slow
check when that is the current criterion's sole oracle. CI retains the complete
Release gate; documentation and purity feedback use fast tests.

Intersect user filters with lane filters. Every maintained Native configuration,
the mutation supervisor and the staged mutation gate must exclude both groups.
The supervisor freezes its effective configuration for Native and rejects a
slow test in the resulting catalogue before admitting reusable evidence. Bind
the lane-policy bytes and effective filter into the mutation fingerprint.

## Consequences

No C# source, assembly membership, assertions or parameterized rows change.
Raw unfiltered `dotnet test` remains an all-lane operation. Use the project
drivers for routine feedback. Node.js, already required by repository CI and
documentation, is now required by both local test drivers to share the policy.

Slow tests provide ordinary regression evidence rather than mutation evidence.
The user's subsequent global rule requires ordinary per-lane coverage to exclude
source sections covered only by slow tests, while shared/fast-covered sections
remain eligible and truly uncovered sections remain visible. This selection
slice does not implement or calibrate that source-exclusion adapter. RT-021
tracks it, repeated timing checks before future lane moves, and bounded disposal
of mutation output as follow-up; no fresh Native adequacy is claimed here.
Preserve production scope and thresholds until measured section exclusions are
calibrated. Existing mutation baselines are invalidated by policy, configuration
and supervisor changes.
