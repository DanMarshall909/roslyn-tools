# Flo dogfooding: duplicate-analysis test-project detection

This experiment delivers one criterion toward [GitHub #118](https://github.com/DanMarshall909/roslyn-tools/issues/118), originally FB-375: default duplicate analysis excludes projects referencing the primary xUnit, xUnit v3, NUnit or MSTest assembly identity. Explicit inclusion restores their occurrences deterministically, production duplicates remain, and the existing name fallback remains effective.

Issue #118 remains open. Neutral-name projects identified only by `IsTestProject` or `Microsoft.NET.Test.Sdk`, without those primary framework references, require another criterion. This change does not claim complete metadata-based test-project detection.

## Candidate and checks

- Flo cycle: `20261008110105787-be70cbfc`.
- Verified code checkpoint: `89dbf406c1e024cbde1736a1282a21a3de1001e0`, extending `bf63bd7f7dfc1fa338cbbb6b392b634e50cfaaf0`.
- Approved source fingerprint: `9C13625650497C929ED4836EF308F05822E5D8E2F9989A014F077FF6D7E367B0`.
- Genuine reviewed RED: the named framework-exclusion assertion fails on the baseline. The complete owning test patch is unchanged through GREEN, COVERAGE and REFACTOR.
- Release solution gate and formatting pass: 4,405 core tests and 308 MCP tests pass; nine existing core tests have explicit skip reasons. Flo reports no uncovered changed line or missing changed file.
- Build-purity tests: 8 pass. Real restore, Debug build and both test projects pass, with authored checkout files and disposable profile unchanged outside declared output/cache paths.
- Real CLI: default min-six scan has 16 groups, zero MCP test occurrences and 34 production occurrences; explicit inclusion has 316 groups, 92 MCP occurrences and the same 34 production occurrences. Repeated default JSON is identical. The baseline had 88 MCP occurrences.

## First mutation run and required scope repair

The first pinned RT wrapper run executes Stryker.NET 4.14.1 over the entire `FindDuplicatesService.cs`. Although the configuration names the core test project, actual solution-mode discovery expands to core and MCP assemblies: 4,385 core and 308 MCP catalogue IDs, including 197 linked MCP IDs. Independent review requires a final run with explicit end-to-end exclusions; this first run does not satisfy that selection gate. The actual CLI checks are separate boundary evidence; the named in-process regression has source-resolvable killing attribution for guard faults.

| Outcome | Whole service | Direct changed span |
| --- | ---: | ---: |
| Killed | 102 | 12 |
| Survived | 32 | 0 |
| No coverage | 13 | 0 |
| Timeout | 3 | 0 |
| Compile error | 52 | 1 |
| Ignored | 23 | 1 |

Two enclosing block mutants also have compile-error outcomes. The direct ignored block is covered by Stryker's block-removal optimization; the corresponding continue-removal fault is killed. Compile errors and timeouts are not counted as rejected behavioral faults here. The report has 4,693 catalogued tests, 410 linked source-resolvable test IDs, no unresolved IDs, and 137 scheduled execution outcomes.

All 48 surviving, uncovered or timed-out findings lie in unchanged code. They expose inherited gaps around scope pruning, ignored/document windows, maximal-window boundaries, score/order, diagnostics and occurrence metadata; this criterion does not establish whole-service readiness. Three timeout faults alter loop progress or termination. They remain distinct from kills. No exclusions or thresholds were weakened to remove findings.

Original native receipt SHA-256: `f40500a393caffaa3f66d60cafa4f60ba92a89b740dae4de084916975698d9d9`. Report SHA-256: `23b64ad1886daca06a7f9e9fdf93e7b8d227161ef32c163b1253154e78c6bdbe`. Native execution exits zero, drains streams, retains no workers, and confirms unchanged bound inputs. A normal non-incremental Release solution rebuild passes afterward.

## Qualified final mutation run

The final native run uses the same immutable code and tests at `89dbf406`, the checked-in RT wrapper and the entire owner source. Its private effective configuration is byte-identical to the reviewed `stryker-find-duplicates-config.json` (SHA-256 `7bd5bf0ca77f183b9d4254f4da33fc703f7c8fcdd3531aaf1f45316a0ad3c715`). The selector configuration is reviewed after the Flo code checkpoint; it does not alter the approved C# or test patch.

The complete actual test catalogue matches the documented projected multiset of 4,212 owning-core cases, including names, source paths, locations and theory multiplicity. All 173 identified core end-to-end cases and the unintended 308 MCP cases are absent. The whole-service and changed-span outcome counts match the first-run table. There are 212 linked source-resolvable test IDs, no unresolved attribution, and eight named in-process guard killers, including the new regression. Inherited findings remain disclosed above. No mutation source exclusions or thresholds change.

Final receipt SHA-256: `21493219c5b5752a20627b04743fbadbca691c3d9f742023c686a7723a1c4657`. Report SHA-256: `36e0ef525c86ccc6cc9c4d67a158dc798aa83682fd5e11604628296b4c3abc09`. Native and wrapper exit zero, inputs remain bound and unchanged, streams drain, and no live owned worker remains. The raw terminal receipt retains one state-Z identity with zero RSS; this satisfies the checked-in wrapper's terminal-process contract. A normal non-incremental Release rebuild passes afterward.

The qualification helper initially rejected that zombie identity with an overly strict empty-process-list assertion. The failed helper and Flo attempt remain intact. A separate Flo execution verifies the retained final native evidence, complete catalogue multiset, input hashes, pinned tools and rebuild, and completes with owned source unchanged. It does not relaunch native mutation or rewrite a failed receipt.

## Timing and resources

The named test takes about 1.6 seconds. Five complete core-suite TRX intervals total 2,132.02 seconds (35.5 minutes), excluding builds and agent/controller time. The first native mutation run takes 1236.862 seconds (20.6 minutes); the correctly selected final run takes 587.060 seconds (9.8 minutes). Both execute 137 scheduled mutants in about 116 seconds. The observed difference is in startup and coverage, with different test selections on a shared machine. It is not a controlled Flo-versus-normal comparison.

These are observations on one shared machine. There is no controlled normal-development comparison, repeated full-run variance sample, or whole-controller token accounting. The experiment does not establish a speedup. Keep recurring verification cost separate from the one-time Flo guidance-link, FluentAssertions RED and reason-bearing skip fixes (FLO-041, FLO-042 and FLO-043), and from operator retries.

Sampled process-group peak RSS is 5548064 KiB (5418.03125 MiB) in the first run and 5638036 KiB (5505.89453125 MiB) in the qualified final run. `.ai-rules.json` records the larger rounded peak and retains the 8 GiB active-worktree reservation as headroom. These measurements cover solution builds and whole-service mutation with the two documented test selections; they do not predict other workloads.

## Recovery and retained evidence

The operator first used an incompatible checkpoint subject, then corrected the unpublished commit to Flo's required subject without changing its tree. An original commit backup remains. A CLI check also initially used the wrong assembly filename; the verified check uses `rt.dll`.

The native run omitted Flo's required `--generated-output` declaration. Native verification succeeds, but Flo correctly reports source drift when the new report files appear in its snapshot. All five generated output files are moved to an evidence archive with verified hashes and an original-to-retained path map. Raw native and failed Flo receipts remain unchanged. A separate generic Flo execution verifies the retained full-run receipt/report, configuration, current input hashes, candidate, pinned tools and test attribution, then completes with source unchanged. This is evidence verification, not a second native run or a rewritten success record. Its first helper attempt rejected Stryker's zero-mutant source placeholders; the corrected helper matches the native reader and retains that failure too.

A first post-review `cycle merge` fast-forwards the local feature branch to the verified code, then rejects the outer fingerprint because the experiment evidence directory also counts as source. There are 104 extra evidence files and zero missing or changed checkpoint inputs. The earlier review approval is invalidated without a push. Temporarily relocating the evidence under excluded `.flo` allows the supported merge to validate the exact checkpoint; all 111 files, including the preserved review bundle, are hash-verified during relocation and restoration to their original paths. Local integration then succeeds. A renewed review covers this recovery and the remaining publication steps.

Local raw evidence, review manifests and path mappings are under `/tmp/rt-flo-find-duplicates-retry-20261008/artifacts/flo-dogfood/`. Flo owns the current and mutation worktrees beneath that checkout. The cancelled first cycle at `/home/dan/code/roslyn-tools/.flo/worktrees/current-cycle`, its dirty source and warm outputs are preserved as experiment evidence. No final cleanup deletes reusable build trees.

## Selection finding

Pinned Stryker 4.14.1 [solution-mode resolution](https://github.com/stryker-mutator/stryker-net/blob/29cf66d848ae6d0d9272f86348e3a4eb57008158/src/Stryker.Core/Stryker.Core/Initialisation/InputFileResolver.cs#L95) scans all solution projects before considering `TestProjects`. The RT wrapper always supplies the solution. Its nominal test-project configuration therefore does not establish actual test scope. The final recorded private configuration and byte-identical tracked owner configuration use the supported [JSON test-case-filter property](https://github.com/stryker-mutator/stryker-net/blob/29cf66d848ae6d0d9272f86348e3a4eb57008158/src/Stryker.CLI/Stryker.CLI/FileBasedInput.cs#L79).

The required final selection excludes the unintended MCP project and 59 owning-core methods that launch the real CLI host (173 catalogue cases), retaining all 4,212 other core cases. Mixed classes retain their in-process controls. Git fixtures, parser-only checks, bounded architecture/MSBuild checks and test-assembly crash probes remain component tests. The explicit exclusion inventory and complete projected catalogue multiset are retained in `final-native-scope-plan.json`. Actual native discovery confirms 4,212 tests, and the complete report catalogue matches the projected multiset. Real end-to-end behavior remains covered by the separate Release/Debug solution gates and real CLI controls.

Permanent support for declared project selection and explicit end-to-end categories in the RT mutation wrapper remains a follow-up. This repair preserves that wrapper, owner source, production scope, thresholds, concurrency and pinned tools.
