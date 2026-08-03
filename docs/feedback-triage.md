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

## Add accepted work to the backlog

For a genuine defect or sufficiently concrete feature request, add a user story to `ralph/backlog-prd.json` using the repository's normal backlog tooling. Include:

- the observed behaviour;
- the expected behaviour;
- a minimal reproduction where possible;
- acceptance criteria;
- the original feedback ID.

Avoid storing machine-specific absolute paths in repository documentation or backlog entries.

## Mark an item processed

After triage, record the outcome through the CLI:

```powershell
rt feedback process --id FB-001 --resolution "Added backlog story US-123"
```

Use a specific resolution, for example:

```powershell
rt feedback process --id FB-002 --resolution "Duplicate of FB-001"
rt feedback process --id FB-003 --resolution "Already fixed by commit abc1234"
rt feedback process --id FB-004 --resolution "Not actionable: no command line or reproduction"
```

## Optional Claude Code schedule

Claude Code can perform this triage periodically while it is running. Configure the schedule in a repository session with a prompt similar to:

```text
Schedule a durable daily cron to triage rt feedback. Read the pending items from
ralph/feedback.json, verify each report against the current repository, add genuine
defects and concrete feature requests to ralph/backlog-prd.json using the configured
backlog tooling, and mark every reviewed item processed with a specific resolution.
```

Keep these constraints explicit:

- The job must verify feedback rather than blindly promoting every item.
- The job must use the backlog tooling configured on that machine; do not embed a personal absolute path in shared documentation.
- Durable Claude Code cron jobs are machine-scoped and run only while Claude Code is active.
- Recreate or renew the schedule when its configured lifetime expires.
