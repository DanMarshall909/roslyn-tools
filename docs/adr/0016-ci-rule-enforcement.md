# ADR-0016: CI rule enforcement

- Status: Accepted
- Date: 2026-09-09
- Stories: RT-019

## Context

Analysis commands return useful findings but do not consistently make findings a
process failure. Pipelines need an explicit rule policy and a reliable gate.

## Decision

Add the read-only `ci` category and `ICiCommand` marker. Each concrete command still
implements exactly one category marker. `rt ci check` and its root shortcut use a
typed in-process application port, shared with the generic MCP adapter.

Version 1 policies select architecture and method-complexity rules. Reuse their
existing engines and retain all findings; never truncate the enforcement set.
Invalid policies, workspace failures and compilation errors fail closed. Existing
architecture baselines remain annotations and cannot suppress enforcement.

The result's `Passed` field expresses rule compliance. The CLI maps it to 0/1.
MCP preserves the normal distinction between a successfully evaluated noncompliant
result and an execution/input error; consumers inspect `passed`.

## Consequences

The category inventory, markers, schemas and generated help include CI. Existing
analysis commands keep their contracts. The new category requires no bespoke MCP
handler or daemon routing. Complexity analysis gains cooperative cancellation.

Custom rule plugins, severity thresholds and baseline suppression are deferred.
The existing analyzers' documented coverage limitations still apply.

## References

- [CI usage](../ci.md)
- [Architecture overview](../architecture-overview.md)
