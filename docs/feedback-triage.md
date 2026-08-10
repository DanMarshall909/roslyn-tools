# Feedback triage

`rt` records unhandled exceptions and accepts explicit bug reports and feature requests through the `feedback` command group.

This document is for maintainers. End-user feedback capture is described in the root README.

## Storage

`FeedbackStore` resolves the feedback file as follows:

1. Starting at the current directory, walk upwards until a `ralph` directory is found.
2. When found, use `ralph/feedback.json` in that checkout.
3. Otherwise, use `~/.rt/feedback.json`.

A companion `.lock` file protects concurrent writers.

## Inspect pending feedback

```powershell
rt feedback list
rt feedback list --status processed
rt feedback list --status all
```

Each item should be classified as one of:

- a reproducible defect;
- a concrete feature request;
- already resolved or duplicated;
- insufficiently actionable.

Do not turn exception text directly into a backlog item without checking the command, source state, and current behaviour.

## Two ways to move accepted feedback onto GitHub

Both require `gh` installed and authenticated (`gh auth login`) — neither embeds
a token in `rt` or in shared documentation. Pick based on shape, not habit:

- **`rt feedback report --id FB-NNN`** — the default for a single, standalone
  defect or feature request (this is also what a public end user runs
  themselves; see the root README). It searches existing issues, ranks
  candidates by title/category/command similarity, and shows possible
  duplicates before creating anything — re-run with `--yes` to file anyway, or
  `--attach-to <number>` to link instead. It renders one flat issue from the
  entry's structured `--title`/`--category`/`--command` fields plus repro
  details, and never includes `Cwd`, `SolutionPath`, or the raw stack trace.
  Prefer this path; reach for `scripts/github-issues.ps1` only when the item
  is genuinely part of larger, actively-tracked engineering work.
- **`scripts/github-issues.ps1`** (see [Work tracking](../AGENTS.md#work-tracking)) —
  for feedback that belongs under an existing or new *outcome* with its own
  Definition of Done and independently deliverable sub-issues. Search first,
  then either attach a new sub-issue to the relevant outcome or create a new
  parent issue. Include the observed behaviour, expected behaviour, a minimal
  reproduction where possible, independently testable requirements, and the
  original feedback ID. Avoid storing machine-specific absolute paths in issue
  bodies.

**Never run either of these against a human's feedback without a human having
asked for it.** See [AGENTS.md](../AGENTS.md#rt-feedback-report-is-a-separate-human-only-concern).

## Mark an item processed

`rt feedback report` marks the entry processed automatically once it files or
links an issue. For anything triaged through `scripts/github-issues.ps1`
instead, record the outcome manually:

```powershell
rt feedback process --id FB-001 --resolution "Tracked by GitHub issue #123"
```

Use a specific resolution, for example:

```powershell
rt feedback process --id FB-002 --resolution "Duplicate of FB-001"
rt feedback process --id FB-003 --resolution "Already fixed by commit abc1234"
rt feedback process --id FB-004 --resolution "Not actionable: no command line or reproduction"
```

## Optional Claude Code schedule

Claude Code can perform this triage periodically while it is running. This is
the one durable exception to "never call `rt feedback report` automatically"
in AGENTS.md — it applies because a maintainer explicitly configured and
authorized the recurring job, not because an agent decided on its own to
publish feedback. Configure the schedule in a repository session with a
prompt similar to:

```text
Schedule a durable daily cron to triage rt feedback. Read the pending items from
ralph/feedback.json, verify each report against the current repository, then for
each genuine defect or concrete feature request run `rt feedback report --id
<id>` (or attach it under scripts/github-issues.ps1 if it belongs to a larger
tracked outcome instead). Mark every reviewed item processed with a specific
resolution.
```

Keep these constraints explicit:

- The job must verify feedback rather than blindly promoting every item.
- The job must use authenticated GitHub tooling configured on that machine; do not embed credentials or a personal absolute path in shared documentation.
- Durable Claude Code cron jobs are machine-scoped and run only while Claude Code is active.
- Recreate or renew the schedule when its configured lifetime expires.
