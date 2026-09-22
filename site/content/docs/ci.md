---
title: Enforce rules in CI
description: Enforce checked-in architecture and complexity rules against your C# solution, with structured findings and a failing exit status for violations.
order: 8
section: CI rule enforcement
---

Use `rt ci check` to enforce repository rules in your build pipeline. Checks report findings without changing source files.

## Create a policy

Check in `.rt/ci.json` under your solution directory. Start with a maximum method cyclomatic complexity:

```json
{
  "version": 1,
  "rules": [
    { "id": "maintainability.complexity", "maxCyclomatic": 10 }
  ]
}
```

A method at the limit passes; a method above it fails. Checks include all findings without top-N truncation and use the existing analyzer's generated-code exclusions.

To enforce architecture rules too, add this entry to `rules`:

```json
{ "id": "architecture", "config": ".rt/architecture.json" }
```

Define that file using the [architecture policy format](https://github.com/DanMarshall909/roslyn-tools/blob/main/docs/check-architecture.md). Architecture baselines label existing findings; they do not make violations pass.

## Run checks

After [installing the CLI](/docs/getting-started/), setting up the solution's SDK, and restoring dependencies:

```sh
rt ci check --solution MyApp.slnx --config .rt/ci.json --json --trust-repository
```

`--config` defaults to `.rt/ci.json`. Configuration paths are relative to the solution directory and must remain inside it. Use explicit trust only for repository code approved for evaluation by your pipeline: loading MSBuild projects may execute repository-controlled build logic.

- Exit **0** means every configured rule ran and produced no findings.
- Exit **1** reports rule violations, with the full structured result on stdout.
- Invalid policies, compilation errors, and incomplete workspace loading also return a **nonzero** exit status.

Results include `passed`, `findingCount`, `checkedProjectCount`, per-rule summaries, and findings sorted by rule ID, file, line, and message. Unknown properties, unknown or duplicate rule IDs, and invalid thresholds are rejected.

## Add a pipeline step

In GitHub Actions, after the installation, SDK setup, and restore steps:

```yaml
- name: Enforce repository rules
  run: rt ci check --solution MyApp.slnx --json --trust-repository > rt-check.json
- name: Upload rule report
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: rt-rule-report
    path: rt-check.json
```

Keep the check step's failing status so violations block the pipeline; do not add `continue-on-error`.

The [MCP host](/docs/automation-and-mcp/) also exposes the same typed operation as `check`. A completed check can return `passed: false` without being an MCP tool error, so MCP consumers must inspect that field.
