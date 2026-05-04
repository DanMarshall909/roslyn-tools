---
id: rt-008
title: rename command must accept symbol FQN from stdin JSON for pipe chains
type: Task
area: commands
status: open
created: 2026-05-04
---

## Summary

`rt refactor rename` currently only accepts the target symbol via positional `<symbol>` argument. To enable pipe chains like `rt find-refs Foo | rt rename Bar`, the rename command must also accept a `fullyQualifiedName` field from a JSON object on stdin when stdin is not a TTY.

## Expected behaviour

```
rt inspect find-refs --symbol WorkspaceLoader --json | rt refactor rename --new-name SolutionLoader --dry-run
```

When stdin is piped and contains a JSON object with `fullyQualifiedName`, rename uses that as the symbol target. Positional arg takes precedence if provided.

## Acceptance criteria

- [ ] `rename` reads stdin when `!Console.IsInputRedirected` is false
- [ ] Parses first JSON line for `fullyQualifiedName` field
- [ ] Falls back to positional `<symbol>` argument if stdin produces no FQN
- [ ] Works end-to-end: `rt find-refs X --json | rt rename Y --dry-run` produces correct diff
- [ ] No behaviour change when stdin is a TTY (non-pipe invocation)

## Context

Discovered in session `ralph/sessions/2026-05-04-daemon-protocol.md` Step 4.
This is the first of the "pipe protocol" tasks that will make rt commands composable.
