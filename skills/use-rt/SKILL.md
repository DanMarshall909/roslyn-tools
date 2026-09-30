---
name: use-rt
description: "Use rt for C# symbol inspection, navigation, dependency analysis, and refactoring before manual Read/Edit. Triggers on: /use-rt, use rt, roslyn refactoring, rt command."
---

# Use rt for C# work

`rt` uses Roslyn to resolve C# symbols and their references. Install with
`dotnet tool install -g roslyn-tools-rt`; the current tool targets .NET 10.
In a checkout, use its wrapper or
`dotnet run --project src/RoslynTools.Cli/RoslynTools.Cli.csproj -- <arguments>`
when `rt` is not on PATH. Build from the current source before validating changes.

## Discover only what the task needs

1. `rt prime` gives the short session primer without loading a workspace.
2. `rt suggest "<task in plain English>" --top 3 --llm` ranks candidate commands.
3. Read the selected command's `--help` for exact parameters and apply controls.
4. Use `rt dump-help --compact` for a wider catalogue, or `rt dump-help` for the
   generated full reference. Do not maintain a second command list in this skill.

MCP offers `prime` with `{}` and `suggest_command` with
`{"query":"<intent>","top":3}` before selecting or trusting a repository.
Use the returned tool schema to form arguments; collections are JSON arrays.
Some tools require explicit repository trust and an absolute solution path.
Trust is a user decision; do not grant it merely to make discovery work.

## Inspect, preview, apply

Use `rt inspect outline <file.cs>` or `rt inspect describe <symbol>` before
reading or editing existing C# source. Use relationships to find semantic
callers/references. Pass `--solution <path>` explicitly when targeting matters.
Prefer bounded symbol/file/project requests and `--llm` for reading or `--json`
for machine parsing when the command supports them. Keep completeness warnings
visible; incomplete results cannot prove absence of references.
Typed inspection results summarize workspace inputs and repeated diagnostics by
default. If a partial result needs a full audit, rerun the same command with
`--full-workspace-evidence` and save its potentially large JSON output to a file.

Before each refactoring, start at a clean commit: record HEAD and full Git status,
including untracked files. Use the supported dry-run/preview, review the complete
proposal, and require unchanged HEAD and empty status again immediately before
apply. A dry-run that writes files is a tool defect: preserve its changes as
evidence and investigate before applying. Test and commit each coherent change
before the next refactoring, including manual extractions.

Check the command's help: some mutators default to preview and require
`--dry-run false` to apply. Omitting `--dry-run` does not necessarily write.
Durable prepare/apply commands have their own reviewed-plan protocol.

If rt cannot express the change, record the gap and use a bounded manual change
under the same caller, behavior and clean-commit checks. A safety refusal or
incomplete acquisition requires investigation before any fallback mutation.

## Reflect without interrupting the task

Quietly aggregate repeated calls, excessive response size, discovery mismatches,
missing operations, preview/apply differences and manual fallback friction in
an existing authorized task or tool-feedback record. Measure bytes/call counts
when discussing savings; preserve safety and completeness diagnostics.

Use `rt maintainer feedback --help` for the current feedback workflow. Capture
findings between operations, not after the last clean check before apply.
Continue ordinary work and report material findings together at handoff. Pause
only affected actions when correctness, safety, required evidence or meaningful
progress is at risk. Propose new standing-guidance changes for owner review.
