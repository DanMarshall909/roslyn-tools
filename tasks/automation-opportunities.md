# Automation Opportunities

Multi-step manual sequences that repeated. Each entry is a candidate for a small script, skill, or alias — but entries earn implementation by a 3rd repetition (or an explicit user ask), not by being logged once.

Style mirrors `dogfood-gaps.md`: one bullet per entry, `- YYYY-MM-DD: <pattern> — <proposed script shape>`.

---

- 2026-04-23: Appending a story to `ralph/prd.json` via bash heredoc + `python -c` failed twice this session on `%` and `${...}` in content. Fell back to writing `ralph/_add-stories.py` then deleting it. Proposed: `ralph/scripts/append-story.py <yaml-file>` — reads a YAML story file (title, description, notes, ACs), auto-increments `id`/`priority`, writes to prd.json, regenerates `prd-visualization.html` in one step. Complements the existing `/add-tasks` skill.

- 2026-04-23: Tour-store backend bounce done 4 times this session — `kill PID on :5000` → `dotnet build` → `source .env` → `dotnet run --urls http://localhost:5000 &` → poll `/api/locations` until 200. Each cycle repeated the same bash+PowerShell incantation. Proposed: `scripts/bounce-backend.sh` — single command, loads `.env`, waits for health-check, prints background task ID. Saves ~20 lines of inline tool calls per cycle.

- 2026-04-23: Moving an issue from `docs/issues/triage/` to `docs/issues/resolved/` is a known workflow (per the escalation rule in user CLAUDE.md) but currently done by hand: `git mv`, append a resolution note citing the commit SHA, commit. Proposed: `/resolve-issue <triage-file> <commit-sha>` slash command — moves the file, prepends a resolution block (`**Resolved:** <date> in <sha>\n\n<note>`), stages the move for commit. Useful on every bug closure.
