---
tags:
  - ci
  - rules
ticket: rt-019
severity: Medium
status: Resolved
area: commands
reported: 2026-09-09
resolved: 2026-09-09
summary: "Add a CI module for enforcing configured rule checks"
---

# CI rule enforcement

## Acceptance criteria

- [x] Expose `rt ci check` through a typed CLI/core/MCP operation.
- [x] Enforce checked-in architecture and cyclomatic-complexity policies.
- [x] Return deterministic findings and nonzero CLI status on violations.
- [x] Reject invalid policies and incomplete checks instead of returning success.
- [x] Preserve baseline visibility and existing analysis behavior.
- [x] Pass focused, architecture, MCP and full local verification.

## Scope

Version 1 supports architecture and maintainability.complexity rule families.
No arbitrary script execution, baseline suppression or custom rule plugins.

## Verification

- Full `scripts/check-local.ps1 -Full` gate passed: zero-warning build, 1,904
  CLI/core tests and 119 MCP tests passed, nine existing skips, formatting verified.
- A real MSBuild fixture returned exit 0 at the complexity limit and exit 1 above
  the limit, with matching structured results.
- Command discovery, category markers, schemas, MCP trust/error behavior,
  malformed policies, baseline enforcement and cancellation are covered.
- Delivery branch: `feat/ci-rule-checks`; integration is tracked by its pull request.
