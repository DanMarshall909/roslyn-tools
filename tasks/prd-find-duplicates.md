# PRD: Duplicate Block Discovery and Semantic Clone Search

## Introduction

Extend `rt` with a new duplicate-discovery workflow that can find repeated or near-repeated code blocks without requiring the user to supply a seed example first.

Today, `find-pattern` is example-driven: the user selects one known block and asks `rt` to find structural matches elsewhere. That is useful once a developer already suspects duplication, but it does not solve the earlier problem of discovering duplication in a large codebase.

This PRD proposes a new read-only command family centered on `find-duplicates`. The first phase uses deterministic Roslyn-based normalization and grouping to surface exact structural duplicates. A second phase adds optional vector-embedding ranking to find semantically similar blocks that are not exact normalized matches. Automatic rewriting remains out of scope for the semantic phase; any future extraction flow must still pass deterministic safety checks.

---

## Goals

- Let developers find duplicated statement blocks without providing a sample block up front
- Surface both exact structural clones and likely near-duplicates
- Rank duplicate groups so the highest-value refactoring opportunities appear first
- Keep results explainable enough that a developer can trust them
- Provide a future bridge into `extract-method --replace-duplicates`

---

## Non-Goals

- Automatically rewriting semantic matches without deterministic validation
- Replacing `find-pattern`; this complements it
- Detecting every possible clone type in v1
- Whole-repo AI summaries of "all duplicated code" without file/line evidence
- Building a cloud dependency into the default local workflow

---

## User Problem

Developers often know a codebase contains duplicated logic but do not know where the best extraction candidates are. Existing tooling is usually weak in one of two ways:

- It requires the user to already know the seed block to search from
- It finds textual duplication but misses renamed variables, reordered trivia, or near-duplicate business logic

The desired experience is:

- "Show me blocks of code that appear multiple times"
- "Show me the most extractable duplicate groups first"
- "Optionally show me semantically similar blocks, not just exact clones"

---

## Proposed Commands

### `find-duplicates`

Primary discovery command for duplicate statement blocks.

Example invocations:

```powershell
rt find-duplicates --solution RoslynTools.slnx
rt find-duplicates --solution RoslynTools.slnx --min-statements 4 --min-occurrences 2
rt find-duplicates --solution RoslynTools.slnx --scope RoslynTools.Commands
rt find-duplicates --solution RoslynTools.slnx --json
```

### `find-duplicates --mode semantic`

Optional semantic mode that uses vector embeddings to rank likely near-duplicates after structural candidate generation.

Example invocations:

```powershell
rt find-duplicates --solution RoslynTools.slnx --mode semantic
rt find-duplicates --solution RoslynTools.slnx --mode semantic --top 25 --json
```

---

## Design Principles

- Deterministic first: structural duplicate detection is the baseline and must work without ML
- Explainability over magic: results should say why blocks were grouped
- Read-only by default: discovery is separate from mutation
- Roslyn-native: use syntax and semantic models, not regexes over source text
- Safe handoff: semantic suggestions must be validated before any future automated extract flow

---

## Implementation Phases

**Phase 1 - Structural duplicate discovery**

- Enumerate candidate statement windows from method bodies
- Normalize identifier, literal, and trivia differences
- Group identical normalized blocks
- Rank and print duplicate groups

**Phase 2 - Noise reduction and usability**

- Suppress overlapping windows when a larger block already explains the duplication
- Add namespace/project/file scoping
- Add JSON output
- Add ranking features such as token count, occurrence count, and extraction plausibility

**Phase 3 - Semantic mode with embeddings**

- Produce embeddings for candidate blocks
- Build nearest-neighbor search over vectors
- Re-rank similar blocks with deterministic structure checks
- Mark semantic matches as suggestions, not rewrite-ready clones

**Phase 4 - Integration hooks**

- Export a duplicate group in a form consumable by a future extract workflow
- Add candidate naming hints and extractability diagnostics

---

## User Stories

### US-201: Discover exact duplicate statement blocks

**Description:** As a developer, I want to find repeated code blocks across a solution without specifying a sample first so I can identify extraction opportunities quickly.

**Acceptance Criteria:**
- [ ] `rt find-duplicates --solution ...` scans the solution and reports duplicate statement groups
- [ ] A duplicate group contains at least 2 occurrences and meets the minimum statement threshold
- [ ] Each occurrence includes file path, line number, containing type, and containing member
- [ ] Output includes occurrence count and a short normalized preview of the block
- [ ] Trivial one-line or boilerplate-only matches are excluded by default
- [ ] xUnit tests cover: exact duplicates in one file, exact duplicates across files, no duplicates, and minimum-threshold filtering

---

### US-202: Normalize renamed identifiers and literals for structural matching

**Description:** As a developer, I want structurally identical code with renamed locals or changed literal values to be grouped together so duplicate detection is useful beyond copy-paste clones.

**Acceptance Criteria:**
- [ ] Variable names do not prevent a match when syntax and data flow shape are otherwise identical
- [ ] Literal substitutions do not prevent a match when the block shape is otherwise identical
- [ ] Whitespace, comments, and formatting do not affect matching
- [ ] xUnit tests cover renamed locals, renamed parameters, and literal changes that should still group

---

### US-203: Reduce overlapping duplicate noise

**Description:** As a developer, I want the command to suppress redundant overlapping matches so the output highlights meaningful duplicate groups instead of hundreds of near-identical windows.

**Acceptance Criteria:**
- [ ] When multiple overlapping windows describe the same repeated block, the command prefers the largest useful block
- [ ] Nested sub-block duplicates are suppressed unless they represent a distinct repeated pattern
- [ ] Output remains stable across repeated runs on the same source
- [ ] xUnit tests cover overlapping windows and deterministic ranking

---

### US-204: Scope and filter duplicate discovery

**Description:** As a developer, I want to narrow duplicate search to a project, namespace, or file set so I can inspect one subsystem at a time.

**Acceptance Criteria:**
- [ ] `--scope` supports project name or namespace prefix filtering
- [ ] `--include-tests` includes test projects; tests are excluded by default
- [ ] `--min-statements`, `--max-statements`, and `--min-occurrences` are supported
- [ ] xUnit tests cover scope filtering and include-tests behavior

---

### US-205: Machine-readable duplicate results

**Description:** As an AI agent or tooling user, I want duplicate groups in JSON so I can rank, filter, and act on them programmatically.

**Acceptance Criteria:**
- [ ] `--json` emits structured output for duplicate groups and occurrences
- [ ] JSON includes a stable group id, ranking score, occurrence metadata, and preview text
- [ ] Empty results return a valid empty array
- [ ] xUnit tests cover JSON shape and empty-result behavior

---

### US-206: Semantic duplicate discovery via embeddings

**Description:** As a developer, I want an optional semantic mode that surfaces likely near-duplicate blocks even when they are not exact structural matches.

**Acceptance Criteria:**
- [ ] `rt find-duplicates --mode semantic --solution ...` returns likely near-duplicate groups ranked by similarity
- [ ] Semantic mode is optional and clearly labeled as lower-confidence than exact structural matches
- [ ] Each semantic result includes similarity score and the structural checks that passed or failed
- [ ] Semantic results are never automatically treated as rewrite-ready
- [ ] xUnit tests cover semantic-mode plumbing and deterministic fallback behavior when embeddings are unavailable

---

### US-207: Extraction-readiness diagnostics

**Description:** As a developer, I want each duplicate group to indicate whether it looks safely extractable so I can focus on actionable results first.

**Acceptance Criteria:**
- [ ] Output includes diagnostics such as parameterizable inputs, outputs, control-flow incompatibilities, or unsupported side effects
- [ ] Groups can be marked `extractable`, `likely_extractable`, or `discovery_only`
- [ ] xUnit tests cover at least one extractable and one non-extractable duplicate group

---

## Functional Requirements

- FR-1: Add a new read-only `find-duplicates` command under `src/RoslynTools/Commands`
- FR-2: Core duplicate analysis lives under `src/RoslynTools/Core`
- FR-3: Structural mode requires no external service and works fully offline
- FR-4: Candidate discovery operates on Roslyn syntax trees and semantic models, not raw text regexes
- FR-5: Matching is statement-block based, not whole-file based
- FR-6: Default output is plain text; `--json` provides structured results
- FR-7: Diagnostics and progress go to stderr
- FR-8: Semantic mode must be optional and must degrade gracefully when embedding support is unavailable
- FR-9: Semantic mode must not change source files or imply mutation safety
- FR-10: Results should include enough metadata to support a future handoff into extraction workflows

---

## Output Shape

Plain-text output should be concise and scannable:

```text
Group 12  occurrences=3  score=0.91  extractable=likely
  preview: validate args; load customer; throw if missing
  1. src/Foo.cs:42 in OrdersService.PlaceOrder
  2. src/Bar.cs:88 in ReturnsService.ProcessRefund
  3. src/Baz.cs:120 in CheckoutService.Submit
```

JSON output should include:

- `groupId`
- `mode` (`structural` or `semantic`)
- `score`
- `occurrenceCount`
- `extractability`
- `preview`
- `occurrences[]`
- `checks[]`

---

## Technical Approach

### Structural Mode

1. Enumerate statement windows from each method body
2. Normalize blocks:
   - strip trivia
   - canonicalize local names
   - canonicalize literal placeholders
   - preserve control-flow shape
3. Hash normalized blocks
4. Group identical hashes
5. Rank groups and suppress redundant overlaps

### Semantic Mode

1. Reuse structural candidate generation
2. Embed normalized or lightly normalized code blocks
3. Query nearest neighbors in vector space
4. Re-rank with deterministic structural checks
5. Emit likely duplicate groups with confidence and reasons

---

## Risks

- Candidate explosion from enumerating every possible statement window
- Too much noise from small or boilerplate blocks
- Semantic mode producing attractive but non-actionable false positives
- Embedding dependency complexity, cost, or latency
- Developers over-trusting similarity scores as proof of safe extraction

---

## Mitigations

- Use sensible defaults for minimum block size
- Suppress nested and overlapping duplicates aggressively
- Keep exact structural mode as the default
- Label semantic results explicitly as suggestions
- Require deterministic extractability checks before any future mutation workflow

---

## Success Metrics

- Developers can find meaningful duplicate groups in a medium-sized solution within one command run
- Top-ranked results are perceived as useful rather than noisy
- Exact duplicate mode finds the current `extract-method --replace-duplicates` fixtures without needing a seed block
- Semantic mode surfaces at least some useful near-duplicates that exact structural mode misses

---

## Open Questions

- Should semantic mode be local-only, provider-backed, or pluggable?
- Should embeddings be generated from raw source, normalized source, or AST-derived text?
- Do we want a separate `find-duplicates-semantic` command, or is `--mode semantic` enough?
- Should duplicate groups be exportable directly into a future extraction command?
- How aggressive should overlap suppression be by default?
