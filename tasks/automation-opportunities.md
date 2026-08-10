# Automation Opportunities

Recurrent work that could be scripted. Two categories:

1. **Multi-step manual sequences** — the same ordered set of commands repeated
2. **Single verbose commands** — run repeatedly and grepped for the same signal

Each entry is a candidate for a small script, skill, or alias — but entries earn implementation by a **3rd repetition (or an explicit user ask)**, not by being logged once. When implemented, the script goes in `scripts/` in the relevant repo and does the extraction itself — runs the command AND pipes through `rg`/`grep` so callers get only the useful signal.

Style mirrors `dogfood-gaps.md`: one bullet per entry, `- YYYY-MM-DD: <pattern> — <proposed script shape>`.

> Entries proposing PRD or Ralph backlog automation are historical and
> superseded by the GitHub parent/sub-issue workflow. Do not implement those
> proposals.

---

## Multi-step sequences

- 2026-05-05: New xunit test project scaffold requires two independent boilerplate items both silent until compile: `<NoWarn>$(NoWarn);CA1707</NoWarn>` in csproj (else CA1707 fires on `with_xxx_yyy` test names) and a `GlobalUsings.cs` with `global using Xunit;` (else `[Fact]` is `CS0246`). Burned 3 build cycles in MCP test scaffold. Proposed on 3rd recurrence: `scripts/new-test-project.sh <project-path>` that creates the csproj from a template and drops a starter GlobalUsings.cs.

- 2026-04-23: Appending a story to `ralph/prd.json` via bash heredoc + `python -c` failed twice this session on `%` and `${...}` in content. Fell back to writing `ralph/_add-stories.py` then deleting it. Proposed: `ralph/scripts/append-story.py <yaml-file>` — reads a YAML story file (title, description, notes, ACs), auto-increments `id`/`priority`, writes to prd.json, regenerates `prd-visualization.html` in one step. Complements the existing `/add-tasks` skill.

- 2026-04-23: Tour-store backend bounce done 4 times this session — `kill PID on :5000` → `dotnet build` → `source .env` → `dotnet run --urls http://localhost:5000 &` → poll `/api/locations` until 200. Each cycle repeated the same bash+PowerShell incantation. Proposed: `scripts/bounce-backend.sh` — single command, loads `.env`, waits for health-check, prints background task ID. Saves ~20 lines of inline tool calls per cycle.

- 2026-04-23: Moving an issue from `docs/issues/triage/` to `docs/issues/resolved/` is a known workflow (per the escalation rule in user CLAUDE.md) but currently done by hand: `git mv`, append a resolution note citing the commit SHA, commit. Proposed: `/resolve-issue <triage-file> <commit-sha>` slash command — moves the file, prepends a resolution block (`**Resolved:** <date> in <sha>\n\n<note>`), stages the move for commit. Useful on every bug closure.

## Recurrent commands (capture-then-search candidates)

- 2026-04-23: `npm run check` followed by grepping for "error" — run 6+ times this session. Proposed: `scripts/check.sh` — runs the check, captures to `/tmp/check.log`, prints only error/warning lines + a pass/fail summary.

- 2026-04-23: `dotnet build src/backend/TourStore.Api/TourStore.Api.csproj --nologo -v quiet | tail -5` — run every backend rebuild (5+ times) to see just the error count. Proposed: `scripts/backend-build.sh` — echo "OK" on success or print only the error lines.

- 2026-04-23: `grep "Hero Checkout|Matched|Failed" <backend-log>` — run every time we bounce the backend to see checkout progress. Proposed: `scripts/tail-checkout-log.sh <task-id>` — tails the background task output filtered to checkout-flow lines.

- 2026-04-23: Extracting API calls from a HAR — wrote an inline `node -e` twice this session to `JSON.parse` a HAR and list its entries. Proposed: `tests/e2e/helpers/inspect-har.ts` — `npm run har:inspect <path>` prints method + status + path + size per entry.

- 2026-04-23: Finding the PID on a given port: `Get-NetTCPConnection -LocalPort N -State Listen | Select OwningProcess`. Run 4+ times. Proposed: `scripts/pid-on-port.ps1 <port>` — one-liner wrapper so it fits in muscle memory.

- 2026-04-23: Applying a rule change — Edit file, git add, git commit with generated message, git pull --rebase, git push, across 1-3 related repos (ai-rules + roslyn-tools + sometimes user CLAUDE.md). Done 4+ times this session in the retro loop. Proposed: `/apply-rule-change` slash command — takes `{target-file, before, after, commit-msg, repos[]}` and runs the full chain with a single confirmation. Saves ~8 tool calls per iteration.

- 2026-04-24: Claude Code's `isolation: "worktree"` Agent parameter branches worktrees from a default base commit (observed: `1c660b6d2` on tour-store, an old pre-rename commit) rather than current HEAD. 5 of 12 workers this session committed at pre-rename paths (`api/`, `client/` vs current `src/backend/TourStore.Api/`, `src/frontend/`), causing directory-rename merge conflicts and forcing 3 stories to be re-run without isolation. Investigate: can the Agent tool accept a `--base HEAD` or similar, or is worktree isolation itself unreliable when the branch differs substantially from the harness's default base? Mitigation added to `execute.md` Phase 2 Step 3 (inspect worker branch base before merging); long-term fix belongs in the harness.

- 2026-04-26: `prd.cjs add` accepts `--acs-file` for a single story, but there's no batch path — multi-story scaffolding (the new `/task` EPIC mode) has to round-trip through a hand-built `tmp-prd.json` + N parallel Opus agents writing `acs-<id>.json` + a Python merge step + `import`. ~5 tool calls of bespoke plumbing per epic. Proposed: `prd.cjs add-batch --from <stories.json>` accepting an array of `{ title, description, notes, epicId, acsFile? | acs? }` entries — auto-assigns ids/priorities, regens progress + HTML once. Folds the merge step into the script.
