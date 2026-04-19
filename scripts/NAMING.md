# Test naming convention

Source: [C:\code\ai-rules\skills\refactor-rules.md](../../ai-rules/skills/refactor-rules.md) (Test Naming section)

## The rule

**`scenario` + `target_name` read together must form a grammatical English assertion about the system's behaviour.**

- `scenario` is the subject/scenario (a gerund phrase with no `When_` prefix)
- `target_name` is the predicate (what the system does in that scenario)
- No `Should_` or `Will_` — state facts
- No class names, method names, HTTP verbs, or status codes

## Good

| scenario | target_name | Combined |
|---|---|---|
| `Renaming_a_type` | `Updates_its_declaration_and_references` | Renaming a type updates its declaration and references |
| `Renaming_a_method` | `Updates_all_call_sites` | Renaming a method updates all call sites |
| `Renaming_in_dry_run_mode` | `Does_not_modify_files` | Renaming in dry-run mode does not modify files |
| `Detecting_circular_dependencies` | `Reports_the_cycle_path` | Detecting circular dependencies reports the cycle path |
| `Detecting_circular_dependencies` | `Returns_nothing_when_the_graph_is_acyclic` | Detecting circular dependencies returns nothing when the graph is acyclic |
| `Generating_a_dependency_graph` | `Honours_the_depth_limit` | Generating a dependency graph honours the depth limit |

## Bad

| scenario | target_name | Why |
|---|---|---|
| `Detecting_circular_dependencies` | `Cycle_path_in_output` | No verb — "Detecting circular dependencies cycle path in output" is not a sentence |
| `Generating_a_dependency_graph` | `Depth_limit` | Noun fragment — "Generating a dependency graph depth limit" is not a sentence |
| `Renaming_a_symbol` | `Apply_changes_added_document_dry_run_does_not_write_file` | Compound predicate that reads as two concatenated sentences |
| `Adding_a_dependency` | `# REVIEW (long): ...` | Generator flagged for manual rewrite |

## REVIEW markers

`scripts/test-renames.csv` uses two markers to flag rows the mechanical generator
can't make read as a sentence:

- `# REVIEW (long): ...` — target_name has more than six words; usually a compound
  predicate that needs splitting or simplifying.
- `# REVIEW (no-verb): ...` — target_name contains no recognised verb; can't form
  a predicate.

`scripts/rename-tests.ps1 -Apply` refuses to run while any `# REVIEW` row remains.
