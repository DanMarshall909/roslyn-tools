---
title: Discover workflows
description: Find the right semantic operation from a plain-language goal, Git context, or the generated command catalogue.
order: 3
section: Workflows
---

Roslyn Tools keeps command metadata in the command implementation and projects it into help, suggestions, and host schemas. Start from the task you need to complete, then inspect the exact command contract before invoking it.

## Ask for a suggestion

Pass a plain-language goal to `suggest`:

```powershell
rt suggest "understand an unfamiliar service and its callers"
rt suggest "rename a public type safely"
rt suggest "find architecture cycles"
```

Run `rt suggest` without a query to classify the current Git changes and recommend relevant next steps. Suggestions never execute their follow-on commands automatically.

## Prime an agent session

`prime` emits a compact guide to the installed command surface:

```powershell
rt prime
```

MCP clients invoke `prime` with an empty object and receive the same structured primer. It is intended for coding agents that need the product's operating rules without loading the complete human-formatted reference.

## Browse the canonical catalogue

Use `dump-help` when you need deterministic command metadata:

```powershell
rt dump-help --compact
rt dump-help --json
rt dump-help --yaml
```

The catalogue includes names, categories, examples, options, maturity, effects, and supported formats. It is generated from the same definitions used by the CLI and MCP host.

## Use command groups

Public operations fall into three intent-oriented groups:

| Group | Purpose | Examples |
| --- | --- | --- |
| `inspect` | Read symbols and source relationships | `describe`, `outline`, `source`, `relationships` |
| `analyze` | Evaluate solution-wide structure and quality | `complexity`, `dependency-graph`, `circular-deps` |
| `refactor` | Prepare or apply source changes | `rename`, `move`, `extract-method` |

Unambiguous commands may also be available directly at the root. Prefer the grouped form in documentation and automation because it makes intent visible.

Continue with [semantic inspection](/docs/inspecting-code/) or [solution analysis](/docs/analyzing-solutions/).
