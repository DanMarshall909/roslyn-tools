---
name: use-rt
description: "Reference guide for the rt Roslyn CLI tool — solution-wide C# rename, refactor, navigation, and analysis. Load this when performing any C# refactoring or symbol-navigation task. Triggers on: /use-rt, use rt, roslyn refactoring, rt command."
user-invocable: true
---

# rt — Roslyn CLI Reference

`rt` is a Roslyn-powered C# refactoring CLI. It performs solution-wide refactoring (rename, move, extract-*, find-refs, etc.) using semantic analysis instead of text matching.

> **Install:** `dotnet tool install -g roslyn-tools-rt` (requires .NET 8 SDK or later). After install, `rt` is on your PATH.

## Command groups

Every command lives under one of three top-level groups. Either form works:

```
rt <group> <command> [args...]   # canonical, e.g. rt refactor extract-method ...
rt <command> [args...]           # root shortcut — works for every command name
```

| Group | Purpose |
|---|---|
| `rt inspect` | Read-only: examine a single symbol or file (`describe`, `outline`, `find-refs`, `find-callers`, `find-impl`, `find-unused`, `find-pattern`) |
| `rt analyze` | Read-only: scan for cross-cutting issues (`circular-deps`, `dependency-graph`, `analyze-interface`, `find-duplicates`, `find-magic-strings`, `list-imports`, `list-smells`) |
| `rt refactor` | Mutations (`rename`, `move`, `extract-*`, `inline-method`, `make-*`, `replace-*`, `convert-*`, `add-*`, etc.) |

## Solution targeting

Most commands need `--solution <path>`. If omitted, `rt` walks up from the current directory looking for a single `.sln` / `.slnx`. Pass an explicit path when there's ambiguity or when invoking from outside the repo.

## Decision guide

| I want to… | Use |
|---|---|
| Rename a symbol everywhere | `rename OldName NewName --solution …` |
| Move type to different namespace | `move --type Ns.Type --to-namespace Ns.New --solution …` |
| Find all references to a symbol | `find-refs --symbol Name --solution …` |
| Find all call sites of a method | `find-callers --symbol Method --solution …` |
| See a type's shape (members, bases) | `describe TypeName --solution …` |
| List all types/members in a file | `outline path/to/File.cs --solution …` |
| Find all implementations of interface | `find-impl IFooInterface --solution …` |
| Find dead code | `find-unused --solution …` |
| Detect circular dependencies | `circular-deps --solution …` |
| Find duplicate code blocks | `find-duplicates --solution …` |
| List code smells | `list-smells --solution …` |
| Extract statements to new method | `extract-method File.cs MethodName --start-line N --end-line M --solution …` |
| Extract class from an oversized type | `extract-class --type Big --members A,B --new-type Small --solution …` |
| Generate interface from class | `extract-interface --type Foo --solution …` |
| Add constructor dependency | `add-dependency --type Foo --dependency IBar --solution …` |
| Add null guards to method params | `add-null-checks --type Foo --method Bar --solution …` |
| Convert class to record | `convert-to-record --type Dto --solution …` |
| Fix mismatched namespace/folder | `fix-namespace --file path/to/File.cs --solution …` |
| Make method async | `make-async --type Foo --method Bar --solution …` |
| Seal a class with no derived types | `make-sealed --type Foo --solution …` |
| Replace DateTime.UtcNow with TimeProvider | `replace-datetime --solution …` |
| Delete symbol with zero refs | `safe-delete --symbol Name --solution …` |
| Inline a trivial helper method | `inline-method --type Foo --method Bar --solution …` |
| Convert static method to instance | `make-instance --method Type.Method --solution …` |
| Convert instance method to static | `make-static --method Type.Method --solution …` |
| Extract params into a Parameter Object | `transform-parameters --method Type.Method --new-type Args --solution …` |
| Implement interface stubs | `implement-interface --type Foo --interface IFoo --solution …` |
| Replace one static call with another | `replace-call --from Old.Method --to New.Method --solution …` |
| Add xUnit test stubs | `add-test --class FooTests --acs "AC1;AC2" --solution …` |
| Find structurally similar code | `find-pattern --file File.cs --start-line N --end-line M --solution …` |
| Visualise type dependency graph | `dependency-graph --scope MyNs --solution …` |

## All commands

| Command | Alias | R/W | Output | Key flags |
|---|---|---|---|---|
| `find-callers` | `callers` | read | text | `--symbol`, `--include-tests` |
| `find-impl` | `impls` | read | JSON | symbol (positional) |
| `find-refs` | — | read | text | `--symbol`, `--kind` |
| `find-unused` | `unused` | read | text | `--scope` |
| `describe` | — | read | JSON | symbol (positional), `--kind` |
| `outline` | — | read | JSON | file or namespace (positional) |
| `circular-deps` | `cdeps` | read | text | `--scope` |
| `dependency-graph` | `depgraph` | read | JSON | `--scope`, `--type`, `--depth`, `--format dot` |
| `list-smells` | `smells` | read | text | `--scope`, `--max-class-lines`, `--max-method-lines`, `--max-params` |
| `find-duplicates` | `fd` | read | JSON | `--min-statements`, `--min-occurrences`, `--scope`, `--include-tests` |
| `find-pattern` | `fp` | read | JSON | `--file`, `--start-line`, `--end-line`, `--scope` |
| `add-dependency` | `ad` | write | text | `--type`, `--dependency`, `--field` |
| `add-null-checks` | `anc` | write | text | `--type`, `--method` |
| `add-test` | `at` | write | text | `--class`, `--name` or `--acs`, `--nested-class`, `--file` |
| `convert-to-record` | `ctr` | write | text | `--type` |
| `extract-class` | `ec` | write | text | `--type`, `--members`, `--new-type`, `--static`, `--output-dir` |
| `extract-interface` | `ei` | write | text | `--type`, `--namespace`, `--output-dir` |
| `extract-method` | `em` | write | text | file (pos), name (pos), `--start-line`, `--end-line`, `--after`/`--before` |
| `fix-namespace` | `fns` | write | text | `--file` |
| `implement-interface` | `ii` | write | text | `--type`, `--interface` |
| `inline-method` | `im` | write | text | `--type`, `--method` |
| `make-async` | `ma` | write | text | `--type`, `--method`, `--no-rename` |
| `make-instance` | `mns` | write | text | `--method` (`Type.Method` form) |
| `make-sealed` | `ms` | write | text | `--type` |
| `make-static` | `mst` | write | text | `--method` (`Type.Method` form) |
| `move` | — | write | text | `--type` or `--namespace`, `--to-namespace`, `--move-file` |
| `rename` | `ren` | write | text | old (pos), new (pos), `--kind`, `--csv` |
| `replace-call` | `rc` | write | text | `--from`, `--to` |
| `replace-datetime` | — | write | text | (solution-wide, no extra flags) |
| `safe-delete` | `sd` | write | text | `--symbol`, `--kind` |
| `transform-parameters` | `tp` | write | text | `--method`, `--new-type` or `--existing-type` |

## Output conventions

- **Read-only commands** (describe, outline, find-impl, find-duplicates, find-pattern, dependency-graph): JSON to stdout — pipe to `jq` or parse directly
- **Mutation commands** (rename, move, extract-*, etc.): plain text diff to stdout
- **All commands**: diagnostics and progress to stderr; results to stdout
- **Exit codes**: `0` success, `1` error, `2` decisions required (see `--decisions <path.yaml>`)

## Rules

- Always pass `--dry-run` first for any mutation command, confirm the diff looks correct, then re-run without it
- All commands support `--dry-run`
- The full, always-current command reference is `rt dump-help` (the binary itself is the source of truth)
- Never block on a failed `rt` call — fall back to manual edits and continue
