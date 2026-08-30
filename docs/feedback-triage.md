# Feedback triage

`rt` records unhandled exceptions and accepts explicit bug reports and feature requests through the `feedback` command group.

This document is for maintainers. End-user feedback capture is described in the root README.

## Storage

`FeedbackStore` resolves the feedback file as follows:

1. Starting at the current directory, walk upwards until a `ralph` directory is found.
2. When found, use `ralph/feedback.json` in that checkout.
3. Otherwise, use `~/.rt/feedback.json`.

A companion `.lock` file protects concurrent writers. The file can remain after a
command exits; exclusivity comes from the open file handle, not from the file's
presence.

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

## Promote accepted work to GitHub Issues

For a genuine defect or sufficiently concrete feature request, preview duplicate candidates first:

```powershell
rt feedback report --id FB-001 --dry-run
```

After reviewing the candidates, either link an existing issue or explicitly create one:

```powershell
rt feedback report --id FB-001 --attach-to 123
rt feedback report --id FB-001 --yes
```

The command records the GitHub issue number and URL on the local feedback entry, so retries do not create duplicates. The generated issue includes:

- the observed behaviour;
- the expected behaviour;
- a minimal reproduction where possible;
- acceptance criteria;
- the original feedback ID.

It deliberately omits machine-specific working directories, solution paths, and stack traces. GitHub mutation requires `--yes` or `--attach-to`; MCP and CLI calls preview by default.

## Mark an item processed

After triage, record the outcome through the CLI:

```powershell
rt feedback process --id FB-001 --resolution "Reported as GitHub issue #123"
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
Schedule a durable daily cron to triage rt feedback. Read pending items through
rt feedback list, verify each report against the current repository, preview duplicate
GitHub issues with rt feedback report --dry-run, and present proposed issue actions for
human approval. Mark reviewed items processed with a specific resolution.
```

Keep these constraints explicit:

- The job must verify feedback rather than blindly promoting every item.
- The job must not create or link GitHub issues without explicit human approval.
- Durable Claude Code cron jobs are machine-scoped and run only while Claude Code is active.
- Recreate or renew the schedule when its configured lifetime expires.
