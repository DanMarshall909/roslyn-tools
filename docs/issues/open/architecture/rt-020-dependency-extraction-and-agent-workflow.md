# RT-020: Dependency extraction and agent workflow

- Status: Awaiting independent completion review
- Authority: accepted implementation plan in the 2026-09-29 development session

## Acceptance

1. Core and Hosting have only approved Microsoft/.NET direct application dependencies. Roslynator remains build tooling; Microsoft's transitive packages remain permitted. Third-party adapters preserve YAML, diff, registration, and host behavior.
2. Agents discover valid CLI/MCP operations through compact shared metadata, workspace-free discovery, executable examples, and current entrypoint guidance.
3. Shared ai-rules requires quiet continuous reflection and aggregate reporting at handoff. Project guidance specializes it; new standing-rule proposals still require review.

## Execution contract

Every refactoring starts at a verified clean commit. Record HEAD and full Git status before its dry-run; require the same HEAD and empty status afterward before applying. Compare preview and actual diff, test, and commit before the next refactoring. Preserve unexpected changes as defect evidence. Manual extraction uses the same clean checkpoints.

Reflection accumulates between operations without interrupting progress, except for active correctness/safety failures or a blocker requiring user authority. Use existing feedback records for tool defects. Raw test and checkpoint evidence is retained under ignored `artifacts/dependency-extraction/`.

## Progress

- Planning: package/usage audit through rt identified DiffPlex and YamlDotNet in Core, Scrutor in Hosting, and Microsoft-transitive Humanizer. Scope and exceptions confirmed.
- Baseline: main is clean at `517a32e`; fetched origin/main matches. Focused characterization passed: 176 Core/CLI and 20 MCP tests.

- Preparation: add the outer adapter project and inward graph; explicitly declare test project dependencies used by their source.

- YAML: removed Core YamlDotNet reference; decision data stays in Core and real YAML persistence is injected at the CLI. Focused checks passed 16 Core/CLI and 8 MCP tests. Manual extraction checkpoint: `0e6ba34`; rt suggestions do not provide a persistence-boundary extraction.

- Renderer interface: rt extraction at clean `b52e9db` changed exactly the two previewed files; post-preview HEAD/status were unchanged. All 154 semantic-diff tests passed.

- Composition: Hosting no longer references Scrutor; adapter registration scans the Core and Adapter assemblies, and semantic-diff consumes its neutral port. 176 Core/CLI and 31 MCP tests passed from clean `447ce36`; scoped format verification passed.

- Renderer move: rt move-slice safely refused incomplete acquisition evidence and left clean `86c3006` unchanged. Used a byte-identical file move after rt caller inspection; 154 Core/CLI and 20 MCP tests passed.

- Solution previews: DiffPlex and the console preview adapter now live outside Core/Hosting. The neutral sink carries document changes; existing output and prepared-write tests passed (181 Core/CLI, 30 MCP). Clean checkpoint `1e63fe7`.

- Dependency guard: evaluated direct/imported package and outward project references fail with RTDEP001/RTDEP002. Six injected violations were RED before enforcement; all seven policy tests and 19 host boundary tests now pass. Compiled Core/Hosting references and the rt architecture graph enforce the inward boundary.

- Workspace-free discovery: explicit suggest metadata now matches its catalogue-only behavior. The production-style empty trust registry regression reproduced InvalidInput before the fix; prime and suggest protocol calls now succeed. All 29 serialization/prime tests pass.

- MCP request discovery: generic sequence inputs now advertise arrays, and request variants carry their applicable fields and common requirements. Three regressions failed before correction; 75 descriptor, move-slice, change-signature, and protocol tests pass. Existing trusted stdio move-slice dry-runs verify advertised arrays deserialize.

- CLI/agent discovery: seven real-catalogue intent cases pass without ranker changes. Actual CLI entrypoint parsing rejects stale feedback examples before correction and accepts both canonical maintainer examples afterward. Refreshed portable skill/entrypoint guidance, compact discovery, preview defaults, clean checkpoints, and quiet reflection. All eight discovery tests, six agent-hook tests, three skill validations and reference generation pass.

- Coverage preparation: 64 Core/adapter cases and 109 MCP cases pass. Adapter source reaches 99.5% lines (only the existing unknown-enum fallback is unexecuted); decision persistence/store and solution diff renderer reach every line. Added preview argument/disk invariants, encoding, hunk-boundary and YAML compatibility checks. Separate Stryker targets preserve coverage after assembly moves.

- Full-gate repair: the new build target exposed ambient repository inputs in the workspace-cache fixture. Baseline six cache tests pass; the original fixture inherited repository targets through deleted project and TPA paths. Isolated its project/solution and runtime-only metadata; all six cache assertions pass without relaxing production fingerprint safety.

- Documentation gate: reviewed the changed Core contracts and implementation fingerprints, documented copying/error/preview semantics, and updated only the affected review-index entries. Both public-Core documentation checks pass.

## Final verification

Verified source at `bc059c0` (subsequent candidate preparation changes only this record):

- `bash scripts/check-local.sh --full`: PASS. Debug build, 3,178 Core/CLI tests, 230 MCP tests, nine existing Core/CLI skips, and full format verification.
- `dotnet build RoslynTools.slnx -c Release -v minimal` and `dotnet test RoslynTools.slnx -c Release --no-build -v minimal`: PASS with the same counts.
- `npm test`: PASS, seven content checks, generated command reference/site build, four built-site checks. Six agent-hook tests and three portable skill validations also pass.
- CLI and MCP self-contained `linux-x64` publishes: PASS. Published CLI primer/suggestion/semantic text diff and actual MCP initialize/list/prime/suggest protocol calls: PASS.
- rt production architecture check: six projects, zero violations. The full solution stops safely at an unresolved `FixtureCases` binding in a test source; retain that limitation. Build/package/compiled-reference tests independently cover inner boundaries and host/test references.
- Adapter line coverage: 99.5%; the existing unknown-enum semantic-renderer fallback is unexecuted. Final adapter mutation report: 164 assertion kills, 21 timeouts, one equivalent survivor, no uncovered mutants; compile errors and ignored mutants are reported separately. Scoped Core and MCP reports retain legacy survivors/uncovered branches and do not establish whole-file mutation completeness. All covering/killing test identifiers resolve; isolated calibration faults fail assertions in each owning assembly.
- One formatting-only correction reformatted the newly added anonymous protocol request object; unrelated formatting was preserved.

Raw logs, command/revision/exit metadata, calibration, coverage, mutation attribution and clean checkpoints live under ignored `artifacts/dependency-extraction/`. The exact candidate manifest and independent verdict are retained there for this handoff. Final publication requires that review and unchanged upstream.

## Measured compact discovery

Published binary, same `suggest "find callers of a method" --top 3` request: JSON emits 5,806 bytes; `--llm` emits 1,432 bytes (75.3% smaller). These are byte measurements, not token counts or a universal ratio. Start with `prime` (835 bytes in JSON), then bounded suggestions and scoped help; the full compact catalogue is 6,867 bytes and serves broader discovery. Retain completeness/safety diagnostics when projecting results.

## Aggregate reflection

| Evidence | Narrow owner / proposed improvement | Disposition and trade-off |
|---|---|---|
| `extract-interface` FQN lookup failed; short name worked; preview lists files/members without patch text | rt symbol resolver and preview contract: consistent identities plus optional bounded patches | Recorded for follow-up; keep caller checks and complete Git diff review until supported. Rich previews must remain bounded. |
| No operation expresses extracting persistence behind a port and rewiring host lifetime; `move-slice` refused incomplete acquisition | rt refactoring surface: assess a persistence-boundary or dependency-extraction plan tool | Manual extraction preserved behavior after clean checkpoints. Do not relax evidence safety to reduce manual work. A general planner needs a proved behavioral contract. |
| Relationship responses include large workspace diagnostic envelopes | rt result projection: compact summaries with retrievable diagnostic detail/completeness | Follow-up proposal. Current callers inspect bounded structured data; hiding completeness warnings would make absence claims unsafe. |
| MCP suggest rejected workspace-free input; generic lists advertised strings; variant branches omitted applicable fields | shared command metadata / MCP schema builder | Fixed with actual protocol/schema RED cases. Preserve metadata as the single command authority. |
| Published feedback examples used a stale path; App-only parsing omitted Program's maintainer projection | command metadata / discovery tests / use-rt skill | Fixed and verified through the actual CLI entrypoint. In-process tests remain preferable where they cover the real entrypoint; spawned tests are excluded from mutation attribution. |
| Clean-check and dry-run/application defaults were easy to miss | repo use-rt and refactoring-slices guidance | Added explicit same-HEAD/empty-status checks and exact command help routing. Capture findings between operations, never between the final clean check and apply. |
| New declarative build guard triggers conservative custom-target workspace cache exclusion | rt workspace input fingerprinting | Preserve safety. Follow-up: distinguish proved pure validation targets from evaluation side effects; false reuse would be worse than an extra load. Cache tests now use isolated inputs. |
| Full-solution architecture analysis stops at unresolved test `FixtureCases`; production-only six-project analysis passes | rt acquisition / architecture analysis | Record the full failure and scoped success separately. Imported-package and compiled/project guards provide additional independent evidence; no complete full-solution analysis claim. |
| `check-local.sh` lacks executable mode although examples invoke it directly | repo verification entrypoint documentation or file mode | Follow-up; this session used `bash scripts/check-local.sh --full`. |
| Root Stryker config cannot cover moved adapter/MCP source through the Core project; console status totals obscure compile errors/ignored cases | assembly-owned mutation configs and evidence reader | Added scoped owning configs, calibration and resolved test attribution. Use JSON counts; distinguish timeout from assertion kill. Future legacy CLI/MCP root globs need separate owning targets. |
| Repeated small observations could distract from delivery or become unreviewed standing rules | shared ai-rules reflection + delivery/review skill | Implemented quiet capture/dedup in existing authorized records and aggregate handoff; exceptions only for active correctness, safety, evidence or progress. Future memory/rule changes retain owner authority. No compulsory journal. |

No general rt planner, workspace cache relaxation, or blanket follow-up tool work is claimed complete. These are candidates for owner prioritization; the current dependency boundary and discovery fixes are independently verifiable.

## Shared ai-rules publication boundary

The quiet continuous-reflection policy is implemented and separately reviewed at `a6b8d943d2eb31a71403964406bfc97e1d860a6c` on `work/continuous-reflection` in `/home/dan/code/ai-rules-continuous-reflection`. Generated-policy, installer, scaffold, convention and skill checks pass; an independent scenario review is PASS. This changes the shared reflection rule, reflect skill, and delivery/review procedures together.

That branch remains unpublished because ai-rules requires explicit user push approval. The durable ai-rules checkout's unrelated unpushed commit and five dirty files are preserved. Installed global links still point to that durable checkout; this work does not claim global activation or client reload. Its reconciliation and publication are handed back explicitly rather than overwriting local work.
