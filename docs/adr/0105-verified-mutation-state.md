# ADR 0105: Retain verified mutation state without native incremental reuse

- Status: Accepted
- Date: 2026-10-06
- Related issue: [RT-021](../issues/open/testing/rt-021-mutation-performance-and-agent-token-usage.md)

## Context

RT pins Stryker.NET 4.14.1. Its current Hosting configuration has 71 eligible
mutants. Missing-state and unchanged-state native baseline probes returned exit
zero while discarding all 71 outcomes. A standalone baseline reporter also failed
after producing a complete report, and an owned worker could outlive the parent.
These observations prevent accepting native baseline reuse for this profile.

The user prioritizes inexpensive agent supervision and tolerates useful compute.
Required mutation scopes, thresholds and separate regression/E2E gates remain
authoritative. Rewriting mutation selection or reconstructing a whole score from
changed subsets would introduce a second mutation engine.

## Decision

Provide `scripts/mutation.mjs`, discoverable through `npm run mutation` and the
agent playbook. It runs the explicit whole configuration through the pinned local
tool. Full results require terminal source-resolved mutations, a source-bound
named test catalog and killer attribution, scheduled-count/version agreement,
unchanged input bindings and no live owned workers. Native nonzero exits and
interrupted or ambiguous reports never seed reusable state.

Persist one atomic bundle per repository/checkout/branch/configuration target in
the per-user cache. Include the exact report, complete verification receipt and
findings. Fingerprint authored source/tests/shared inputs, tool manifest, SDK,
resolved packages, build settings, wrapper and execution environment. Shell
bookkeeping is removed from the environment actually passed to the producer.

Fast mode reuses identical verified state without launching Stryker. Changed,
missing, corrupt or incompatible state fails with an explicit full-seed command.
This deliberately requires full target refreshes for changed tests and source.
It does not claim calibrated native incremental behavior or cross-checkout reuse.

The Linux supervisor owns a process group, records PID/start-time identities,
retains raw diagnostics and sampled resources, and forwards cancellation before
bounded escalation. Native terminal exit triggers cleanup of lingering owned
workers. Cleanup does not invalidate already complete source-bound outcomes when
streams drain and all workers stop. Elapsed time alone never cancels a live run.

## Consequences

Identical-state feedback requires no mutation execution. Changed-state work pays
the full target cost; the compact Hosting profile remains useful for that cost.
The wrapper preserves native results and thresholds, without claiming feature
admission for previously incomplete Core/discovery gates. Raw logs remain in
ignored artifacts; durable report/receipt/findings survive their cleanup.

Linux execution and cancellation need direct calibration. Other platforms,
incremental source/test selection, concurrency tuning and remaining discovery
admission are follow-up work. Controller calls/response bytes and tokens remain
separate from the supervisor's stdout-byte metric.
