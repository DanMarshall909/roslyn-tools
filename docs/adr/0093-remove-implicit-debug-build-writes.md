# ADR-0093: Remove implicit source and profile writes from Debug builds

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0081](0081-bounded-cli-workspace-evidence.md)

## Context

The CLI project attached three targets to every normal Debug build. They
attempted to format changed files, deploy a skill into the user profile, and
rewrite ignored `docs/COMMANDS.md`. On Linux, the first two attempted a missing
`powershell` executable and continued; the docs write succeeded. A clean Git
status therefore failed to reveal all build effects.

## Decision

Remove the three implicit targets. Formatting remains an explicit
`scripts/format-changed.ps1` action, skill deployment remains an explicit
`scripts/deploy-skill.ps1` action, and the command reference is generated from
source-built `rt dump-help` by `npm run generate:commands`. The local gate and
site generator no longer pass properties that merely suppressed the old hooks.
CLI command-registration checks run at startup and in tests, including during
explicit reference generation, rather than as a side effect of building.

## Consequences

- Two plain Debug builds left tracked diff, Git status, ignored command-reference
  timestamp, and a temporary user-profile canary unchanged. The previous plain
  build had rewritten that reference and attempted both PowerShell calls.
- Explicit generation can still rewrite the ignored local reference when
  requested. Build outputs remain under normal `bin`/`obj` paths.
- This removes those build effects; it does not by itself certify the full
  workspace input set as `Complete`. Other authored targets and off-solution
  inputs still require independent analysis in RT-016.
- A durable cross-platform clean-tree restore/build/test test remains open in
  the platform-hardening OpenSpec.
- The existing deploy and changed-file format scripts require PowerShell; this
  Linux environment has no `pwsh`, so only the explicit reference generator was
  exercised here. Portable opt-in script contracts remain open in P1.6.

## References

- [Architecture overview](../architecture-overview.md)
- [Platform-hardening OpenSpec](../../openspec/changes/critical-platform-hardening/specs/platform-hardening/spec.md)
