# ADR-0002: `_suggestions` output contract and proactive session start

- Status: Accepted
- Date: 2026-06-19
- Stories: US-135 (Beads-lessons batch — `_suggestions`, `rt prime`, session hook, `dump-help --compact`, `suggest` git-fallback)

## Context

`rt` was reactive by design: the rt-first gate blocked `Read`/`Edit` on `.cs` files until the agent had run an `rt inspect` command, but it gave agents no positive signal about what to run *next*. Every JSON result was terminal — it answered the current question and stopped. Agents that didn't already know the catalog had to consult `rt dump-help` (2000+ tokens) or `rt suggest` (requires knowing you need a suggestion).

The Beads dependency-tracking tool (`bd`) solved the same orientation problem with three stacked layers: a dependency graph that makes next-steps structurally visible, a `bd prime`/`bd ready` ritual that enforces ordering procedurally, and harness hooks that hold both even as context decays. `rt` had only the reactive gate; the proactive layers were missing.

## Decision

Add three layers to `rt`, mirroring the Beads model:

**1. `_suggestions` in JSON output (graph layer)**

Every `rt inspect describe` and `rt inspect find-refs --json` response includes a `_suggestions` array of five pre-filled follow-on commands — FQN and `--solution` path baked in. Suggestions are computed at the adapter layer (CLI command handler, MCP handler) using `SuggestionHelper`, not inside the core service. The `SuggestionHelper` is a shared static class in `src/RoslynTools/Services/Suggest/` consumed by both host adapters. Related types (extracted from the described symbol's constructor parameters, public method signatures, and public properties/fields) appear as `rt inspect describe` entries in `_suggestions`, making the type graph navigable without any prior knowledge of the catalog.

The contract: any inspection DTO can carry a `_suggestions` init property (`[JsonPropertyName("_suggestions")]`); the property is populated at the adapter layer and is `null` when JSON output is inactive. The field is opt-in per DTO — it is not added to the base DTO or a shared interface, because the suggestion content is command-specific.

**2. `rt prime` (ritual layer)**

New root-level command (not `IRtCommand` — registered at root like `suggest` and `dump-help`). No MSBuild, no solution load. Returns a hardcoded gate rule, entry-point hint, and hot-command list as compact JSON (≈200 tokens) or human-readable text. The hot-command list is static — a deliberate trade-off (see Alternatives).

**3. Session-start hook (harness layer)**

A `UserPromptSubmit` hook in `.claude/settings.json` runs `rt prime --json` once per session (guarded by a `PPID`-stamped marker in `/tmp`). This shifts `rt` from reactive ("you'll discover it when you try to read a `.cs` file") to proactive ("you know the gate rule and entry point before the first tool call").

## Consequences

- **Easier:** Agents discover the right next command without consulting `dump-help` or `suggest`. The graph is self-navigating — each result points to the next. Session context is primed automatically; AGENTS.md instructions are no longer the only recall path.
- **Harder:** Every new inspection/analyze command needs a `SuggestionHelper.For<Command>` overload and adapter-layer enrichment before its JSON output is self-navigating. This fan-out is tracked in backlog stories US-136 and US-137.
- **Invariants:** `_suggestions` is never populated inside core services — only at the adapter layer. This keeps services testable without any suggestion-generation logic, and keeps the solution path (needed to generate pre-filled commands) out of the service API. Enforced by code review; no architecture test yet.
- **Deferred:**
  - US-135: body-call related symbols + frequency ranking (body-walk requires async semantic model per method; only signature-based extraction is implemented).
  - US-136: `rt inspect outline` needs an envelope wrapper to carry `_suggestions` (bare-array JSON is a breaking schema change).
  - US-137: fan-out to all remaining `rt inspect` and `rt analyze` commands.
  - `rt prime` hot-command list is hardcoded; it will drift as the catalog grows. Tracked as a known trade-off in `PrimeService.cs`.

## Alternatives considered

- **`_suggestions` inside the core service:** Rejected — services don't know the `--solution` path or the host format. Generating pre-filled commands inside the service would couple it to CLI/MCP rendering concerns.
- **Shared interface for `_suggestions` on all DTOs:** Rejected — the property content is command-specific; a shared interface would force all DTOs to carry suggestions even when the command doesn't support them. Init property per DTO is lighter.
- **Dynamic `rt prime` from the live catalog:** Would require loading the catalog at hook time, adding startup latency to every session prompt. Hardcoded list is instant; the trade-off is staleness. Accepted because the hot commands are stable (describe, outline, find-refs, rename, move, suggest) and the gate rule never changes.
- **`$SESSION_ID`-stamped hook marker:** Would give true per-session deduplication. Rejected because Claude Code doesn't expose a stable session ID to bash hooks; `$PPID` is consistent within a bash environment and is the closest stable proxy.

## References

- Commits: `6b542c3`, `c334d51`
- Related ADRs: [ADR-0001](0001-core-service-thin-host-adapters.md) (the thin-adapter pattern that makes adapter-layer suggestion enrichment natural)
- Related docs: [pattern-core-service.md](../pattern-core-service.md)
