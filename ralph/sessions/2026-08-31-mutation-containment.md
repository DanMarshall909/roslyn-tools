# Session: 2026-08-31 — Mutation Containment Completion

**Goal:** Finish the interrupted P0.0 slice without widening it into durable mutation delivery.
**Solution:** `RoslynTools.slnx`
**Branch:** `codex/p0-mutation-containment`

---

## Baseline [REAL]

The branch already contained an unfinished containment edit across 37 C# files. The
single invariant was: an apply-capable request must remain local and review-first,
and no daemon ambiguity may lead to fallback or a second execution.

The first focused run exposed seven stale tests/defaults. Those failures were treated
as evidence of incomplete containment, not as a clean baseline.

## Completed slice [REAL]

- Every mutation definition now requires `Review` default and `InProcess` placement.
- CLI mutation shapes require either an explicit `--apply` or default `--dry-run true`.
- Mutation application definitions are absent from ordinary MCP discovery.
- Legacy and typed mutation paths bypass daemon startup, recovery, and fallback.
- Tests that intentionally apply now opt in explicitly.
- The migration manifest records all 33 mutation commands as `in-process` and
  `review-default`.

The durable daemon intake-fence requirement remains open. Preview containment avoids
that boundary by refusing to dispatch mutations at all.

## Verification [REAL]

```text
focused core/CLI: 74 passed
focused mutation regressions: 184 passed
focused MCP/architecture: 17 passed
full MCP project: 82 passed
full core/CLI project: 1787 passed, 9 skipped, 0 failed
dotnet format --verify-no-changes: passed
Release solution build: passed, 0 warnings, 0 errors
openspec validate --changes --strict --no-interactive: 2 passed
git diff --check: passed
```

The nine skips were pre-existing known-defect/contract skips and were not introduced
by this slice. No feedback was submitted externally. No commit or push was made.

## Next

Freeze the unreleased public catalogue against the approved disposition, then remove
or hide unsupported effectful and misleading surfaces before adding new workflows.
