# ADR-0109: Disable repository mutation testing

- Status: Accepted
- Date: 2026-11-05

## Decision

The repository owner has disabled mutation testing. `mutation-testing.json`
records the policy. Supported execution entry points check it before starting
native tools; explicit mutation requests fail with a disabled diagnostic, while
the commit hook explicitly skips the optional mutation gate. Invalid or missing
policy fails closed.

Agents must not run or queue direct Stryker, Flo mutation phases or post-session
mutation for this repository. Ordinary tests, coverage, formatting and independent
review remain required. This supersedes the execution expectation of ADR-0105,
without deleting historical profiles, tooling or retained evidence.

## Consequences

Completion makes no mutation-score claim. Supervisor contract tests may still use
isolated fake producers with their own enabled fixture policy; they never launch
native mutation against this repository. Re-enabling requires an explicit owner
decision and a tracked policy change.
