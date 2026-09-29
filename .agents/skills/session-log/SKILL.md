---
name: session-log
description: "Protocol-first design session: walk through a workflow using real and simulated rt commands, generating expected outputs to discover the right tool contracts before implementing them. Triggers on: /session-log, design session, simulate rt commands, protocol session."
---

# session-log — Protocol-First Design Session

Design rt workflows by declaring commands (real or invented) and generating their expected outputs. The log becomes the spec, the JSON contract, and the integration test fixture.

## When to use

- You need an rt command that doesn't exist yet
- You want to design the full workflow before writing any implementation
- You want to discover what a command's JSON output shape should be

## How it works

0. **Pick up context** — if this is session 2+, read the most recent `ralph/sessions/*.md` retrospective before anything else. Extract the "Next session" items and treat them as the starting goal. Do this before asking the user anything.
1. **Init** — create `ralph/sessions/YYYY-MM-DD-<feature>.md`
2. **Walk the workflow step by step:**
   - Real rt command exists → mark `[REAL]`, run it, paste actual output
   - Command doesn't exist → mark `[SIMULATED]`, generate plausible output
   - Mutations → show unified diffs (real or simulated)
   - **Each step must be internally consistent** — invented symbol names must match across steps
3. **Close** — write `## Retrospective` capturing what was real, what was simulated, and promotion candidates

## Log format

```markdown
# Session: YYYY-MM-DD — <feature>

**Goal:** <what we're trying to achieve>
**Solution:** RoslynTools.slnx

---

## Step N — <command name> [REAL|SIMULATED]

`rt <command> --flags`

<!-- SIMULATED — command does not exist yet -->
<output or diff>

---

## Retrospective

### Real commands used
- `outline`, `find-refs`

### Simulated commands → rt candidates
- `architecture-scan` — high value, feasible as heuristic analysis
- `extract-orchestrator` — feasible for constrained command→handler pattern

### Gaps logged
- YYYY-MM-DD: <command> does not exist — <why it was needed>

### Next session
- <what's left to do>
```

## Rules

- Ask for the feature name if not provided as an argument
- Simulated outputs must be plausible given what Roslyn would actually see in the real codebase — read the relevant source files before generating output
- A symbol invented in step 2 must appear with the same name in step 3's diff
- For simulated mutations, the diff must reflect what the real source file would look like before and after
- Real commands: always `--dry-run` first for mutations
- At close, for each simulated command that represents a missing or broken rt feature, log via `rt maintainer feedback add` with `--description "rt: <command> — <what it should do>"`.
- If a real command exits 0 with no output and no error, also log it via `rt maintainer feedback add`: `--description "rt: <command> returned empty silently — <what was expected>"`.
- **Issue logging is on by default** — write issues inline the moment a gap or blocker is found. Do not batch to the retrospective. The user does not need to ask for this.
- Session files live in `ralph/sessions/` and should be committed — they are specs, not scratch

## Issue logging — write issues inline as they are discovered

Whenever a step surfaces a **gap, blocker, or concrete task**, write an issue to `docs/issues/open/` immediately — do not wait for the retrospective.

**What triggers an issue:**
- A real rt command is blocked or crashes (Bug)
- A simulated command is needed and has clear scope (Task)
- A pre-condition or design constraint is discovered (Task/Bug depending on whether it affects existing commands)
- A cross-cutting concern is found (e.g. a pattern that should apply to all commands)

**How to write it:**
1. Count all files under `docs/issues/open/` and `docs/issues/resolved/` to get the next `rt-NNN` ID
2. Write `docs/issues/open/<area>/<ticket-id>-<slug>.md` using the standard template (see `/issue` skill for template)
3. Mark the step in the session log with `> **[ISSUE: rt-NNN]** written — <one-line summary>`

**Areas to use:**
- `workspace-loader` — bugs in solution loading, `.slnx` support, workspace lifecycle
- `commands` — missing commands, bugs in existing commands
- `architecture` — boundary rules, SoC verification, architectural tooling

**Example inline marker in session log:**

```markdown
## Step 2 — rt outline [REAL - BLOCKED]
...
> **[ISSUE: rt-002]** written — WorkspaceLoader rejects .slnx; rt cannot analyse its own solution
```

## Example invocations

```
/session-log change-signature
/session-log find-unused extraction
/session-log
```
