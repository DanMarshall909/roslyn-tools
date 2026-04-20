# Mutation Testing Pilot — MakeSealedCommand

## Run metadata

- **Date:** 2026-04-20
- **Stryker version:** 4.14.1
- **Invocation:** `dotnet stryker` from repo root (uses `stryker-config.json`)
- **Scope:** `**/Commands/MakeSealedCommand.cs`
- **Test project:** `tests/RoslynTools.Tests/RoslynTools.Tests.csproj`
- **Host:** Windows 11, .NET 8, 4 parallel test sessions
- **Wall-clock runtime:** ~37 seconds (init build + 26 mutant test runs)
- **Pre-existing red test:** `FindDuplicatesTests.Json_link_field_uses_forward_slash_path_with_start_line` (unrelated to MakeSealed; baseline noise)

## Summary

| Metric            | Value  |
|-------------------|--------|
| Mutants generated | 31     |
| Tested            | 26     |
| Killed            | 6      |
| Survived          | 20     |
| Timeout           | 0      |
| No coverage       | 1      |
| Ignored (dedup)   | 4      |
| **Score**         | **22.22%** |

## Surviving mutants — classified

13 survivors point at a single systemic gap: **the CLI wiring layer of every command is untested.** Existing tests call `MakeSealedCore.RunAsync` directly and skip `MakeSealedCommand.Info` / `Build()` entirely.

7 survivors are genuinely equivalent (help-text strings with no behavioural effect).

| #  | Id   | Line:Col | Mutator         | Mutation                                       | Classification   | Action                                                                  |
|----|------|----------|-----------------|------------------------------------------------|------------------|-------------------------------------------------------------------------|
| 1  | 1864 | 11:15    | String          | `Name: "make-sealed"` → `""`                   | Missing test     | Assert `Info.Name == "make-sealed"`                                      |
| 2  | 1865 | 12:18    | Boolean         | `Mutates: true` → `false`                      | Missing test     | Assert `Info.Mutates` is true                                            |
| 3  | 1866 | 13:21    | Boolean         | `JsonOutput: false` → `true`                   | Missing test     | Assert `Info.JsonOutput` is false                                        |
| 4  | 1867 | 14:19    | String          | `Category: "Mutation"` → `""`                  | Missing test     | Assert `Info.Category == "Mutation"`                                     |
| 5  | 1868 | 15:19    | String          | `Aliases: ["ms"]` → `[""]`                     | Missing test     | Assert `Info.Aliases` contains `"ms"`                                    |
| 6  | 1869 | 16:20    | String          | `WhenToUse: "..."` → `""`                      | **Equivalent**   | Help text only; disable                                                  |
| 7  | 1870 | 19:13    | String          | Example string → `""`                          | **Equivalent**   | Help text only; disable                                                  |
| 8  | 1871 | 20:13    | String          | Example string → `""`                          | **Equivalent**   | Help text only; disable                                                  |
| 9  | 1872 | 21:13    | String          | Example string → `""`                          | **Equivalent**   | Help text only; disable                                                  |
| 10 | 1874 | 27:23    | Object init     | `Option<string>(...) { IsRequired = true }` → `{}` | Missing test | Assert command fails when `--type` is omitted                          |
| 11 | 1876 | 27:52    | String          | `"--type"` → `""`                              | Missing test     | Assert `--type` is accepted (and blank isn't)                            |
| 12 | 1877 | 27:84    | Boolean         | `IsRequired = true` → `false`                  | Missing test     | Same as #10 — missing `--type` must error                                |
| 13 | 1879 | 28:61    | String          | `"Path to .sln/.slnx"` description → `""`      | **Equivalent**   | Description only; disable                                                |
| 14 | 1881 | 29:55    | String          | `"Preview without writing"` description → `""` | **Equivalent**   | Description only; disable                                                |
| 15 | 1883 | 31:46    | String          | Command description → `""`                     | **Equivalent**   | Description only; disable                                                |
| 16 | 1884 | 32:9     | Statement       | `cmd.AddAlias("ms");` removed                  | Missing test     | Assert built `Command` exposes `ms` alias                                |
| 17 | 1886 | 33:9     | Statement       | `cmd.Add(typeOpt);` removed                    | Missing test     | Assert built `Command.Options` contains `--type`                         |
| 18 | 1887 | 34:9     | Statement       | `cmd.Add(solutionOpt);` removed                | Missing test     | Assert built `Command.Options` contains `--solution`                     |
| 19 | 1888 | 35:9     | Statement       | `cmd.Add(dryRunOpt);` removed                  | Missing test     | Assert built `Command.Options` contains `--dry-run`                      |
| 20 | 1889 | 37:9     | Statement       | `cmd.SetHandler(...)` removed                  | Missing test     | End-to-end assertion that invoking the command reaches `MakeSealedCore`  |

Totals: **13 missing test, 7 equivalent.**

## Observations

### Runtime

- **~37 seconds** for 26 mutants on a single file. Full test suite runs in ~5 seconds (initial run), and each mutant run reuses coverage-based filtering. CLAUDE.md's "~4 min" estimate is stale — mutation testing is substantially faster than the plan assumed.
- Extrapolating: ~40 minutes for all 31 commands if survivor patterns hold (~30 mutants/command × ~1s/mutant average).
- **Coverage-based filtering**: 4 mutants were dedup'd ("block already covered"), saving a little test time. 1 had no coverage.

### Systemic gap — this is the whole game

All 13 "missing test" survivors on MakeSealedCommand will recur on every other `IRtCommand` in `Commands/`. The CLI metadata + `Build()` shape is dogma-checked at app startup (via `App.cs` reflection), but **never unit-tested**. Mutating Info fields or stripping `cmd.Add(...)` calls doesn't break any test because tests bypass the CLI layer and call `*Core.RunAsync` directly.

**The fix is one test class, not 31.** A parameterised test suite (xunit `[Theory]` + data-driven walk of all `IRtCommand` implementations via reflection) can assert, for every command:

- `Info.Name` non-empty and matches `Build()`'s root `Command` name.
- `Info.Mutates`, `Info.JsonOutput`, `Info.Category`, `Info.Aliases` match expected shape per command.
- `Build()` returns a `Command` whose `Options` include every declared option.
- Every alias in `Info.Aliases` is registered on the built `Command`.
- The handler is wired (invoke with valid args and verify the Core method is called — needs a seam or test doubles).

This collapses ~13 × 31 = ~400 expected survivors across the full `Commands/` rollout into a single well-designed test class.

### Equivalent mutant pattern

Help-text strings (descriptions, examples, `WhenToUse`) have no behavioural effect — the tests don't snapshot `rt --help` output or `dump-help` output. Candidate strategies:

- **File-level disable** via `// Stryker disable all : help text has no behavioural effect` at the top of each `*Command.cs` for string mutations, OR
- **Mutator-level disable** in `stryker-config.json` for the `String mutation` category on `Commands/**/*.cs`. Risky — would hide real string bugs in option names (`"--type"` was a missing-test survivor, not equivalent).
- **Snapshot test `rt dump-help --json` output.** Every command gets snapshotted. String changes now break the snapshot. Cleanest — but adds snapshot maintenance cost.

Preferred: **snapshot `rt dump-help` output** because it both kills the mutants AND becomes a contract test on the help output that users rely on.

### rt dogfood gaps surfaced

Logged to [tasks/dogfood-gaps.md](tasks/dogfood-gaps.md):

- `rt add-test` should be able to scaffold a CLI-layer test for an `IRtCommand` subclass — reading `Info` and `Build()` and emitting assertions. Not a feature today; worth adding.
- `rt dump-help` has `--json` output already — snapshot testing could consume it directly.

### Test suite health signal

- Mutation score 22.22% on MakeSealedCommand.
- 100% of survivors trace to one of two causes (CLI layer untested; help text equivalent).
- No weird survivors, no flaky mutants, no timeouts — the suite is deterministic and Stryker works cleanly on this codebase.

## Recommendation

1. **Proceed to rollout.** The pattern is clear and the work is tractable.
2. **Next story (US-225): kill the CLI-layer gap with one `CommandMetadataTests` class.** Parameterise over all `IRtCommand` implementations. Expect to kill ~13/command across 31 commands in one PR.
3. **Following that (US-226): snapshot `rt dump-help --json` output** to kill the 7 equivalent-text survivors per command without scattering `// Stryker disable` comments. Also gives us contract coverage on help output.
4. **Handler-wiring test (mutant #20) is the hardest** — needs a way to invoke the CLI and verify the Core method was called without actually running the workspace loader. Possibly a test seam or injectable handler. May be worth splitting into its own smaller story.
5. **Skip CI for now** — runtime is fast enough to run locally on demand. Add CI gate after rollout is complete.

Revised effort estimate for 100% score across `Commands/`:
- **US-225 (one test class, parameterised)**: ~1 day. Kills ~400 mutants across 31 commands.
- **US-226 (help-text snapshot)**: ~half a day.
- **US-227 (handler-wiring per command)**: ~1-2 days. Hardest part.

Total: ~3 days of focused work, not the "30 separate command-by-command investigations" implied by the original plan.
