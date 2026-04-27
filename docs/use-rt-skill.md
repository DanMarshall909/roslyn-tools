---
name: use-rt
description: "Reference guide for the rt Roslyn CLI tool (31 commands). Load this when performing C# refactoring, symbol navigation, or code analysis on any ProjectX or roslyn-tools solution. Triggers on: /use-rt, use rt, roslyn refactoring, rt command."
user-invocable: true
---

# rt — Roslyn CLI Reference

`rt` is a Roslyn-powered C# refactoring CLI. Run from `C:\code\roslyn-tools\` using `rt.cmd`.

> **Before using rt.cmd**: the wrapper runs the published binary — publish first if recently changed:
> `dotnet publish src/RoslynTools.Cli/RoslynTools.Cli.csproj -c Release -r win-x64 --self-contained false`

## Command groups

Every command lives under one of three top-level groups. Either form works:

```
rt <group> <command> [args...]   # canonical, e.g. rt refactor extract-method ...
rt <command> [args...]           # root shortcut (works for any unambiguous name)
```

| Group | Purpose |
|---|---|
| `rt inspect` | Read-only: examine a single symbol or file (`inspect`, `outline`, `find-refs`, `find-callers`, `find-impl`, `find-unused`, `find-pattern`) |
| `rt analyze` | Read-only: scan for cross-cutting issues (`circular-deps`, `dependency-graph`, `analyze-interface`, `find-duplicates`, `find-magic-strings`, `list-imports`, `list-smells`) |
| `rt refactor` | Mutations (`rename`, `move`, `extract-*`, `inline-method`, `make-*`, `replace-*`, `convert-*`, `add-*`, etc.) |

The only name collision is `inspect` (group vs. command); use `rt inspect inspect <symbol>` when you need that command. Every other name resolves at the root.

## Solution paths

| Repo | `--solution` value |
|---|---|
| libraries | `C:\code\libraries\ProjectX.Libraries.sln` |
| hero-platform | `C:\code\hero-platform\ProjectX.sln` |
| azure-functions | `C:\code\azure-functions\ProjectX.AzureFunctions.sln` |
| roslyn-tools (self) | `RoslynTools.slnx` (relative, from `C:\code\roslyn-tools\`) |

## Decision guide

| I want to… | Use |
|---|---|
| Rename a symbol everywhere | `rename OldName NewName --solution …` |
| Move type to different namespace | `move --type Ns.Type --to-namespace Ns.New --solution …` |
| Find all references to a symbol | `find-refs --symbol Name --solution …` |
| Find all call sites of a method | `find-callers --symbol Method --solution …` |
| See a type's shape (members, bases) | `inspect TypeName --solution …` |
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
| `inspect` | — | read | JSON | symbol (positional), `--kind` |
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

- **Read-only commands** (inspect, outline, find-impl, find-duplicates, find-pattern, dependency-graph): JSON to stdout — pipe to `jq` or parse directly
- **Mutation commands** (rename, move, extract-*, etc.): plain text diff to stdout
- **All commands**: diagnostics and progress to stderr; results to stdout

## Rules

- Always pass `--dry-run` first for any mutation command, confirm the diff looks correct, then re-run without it
- All commands support `--dry-run`
- If `rt` fails or doesn't support the operation: do the work manually and append a line to `C:\code\roslyn-tools\tasks\dogfood-gaps.md`: `- YYYY-MM-DD: <what was attempted> — <why it failed>`
- Never block on a failed `rt` call — log and carry on
