# Skipped-test disposition for preview admission

Recorded 2026-10-03 for RT-016 and Milestone 0.3. This closes the inventory and
assignment task, not the underlying fixes, test removals, or preview admission.

## Evidence scope

The retained full Release gate at revision
`158d43908efa99e0634eae7af3ea6ef373c47b5c` ran:

```sh
bash scripts/check-local.sh --full --configuration Release --results-directory <results-directory>
```

Its core TRX has nine `UnitTestResult` rows with outcome `NotExecuted`; its MCP
TRX has none. The TRX aggregate counter says `notExecuted=0`, so the inventory
uses actual result rows and their `TestMethod` identities, not that counter.
The `SymbolFormatter` source fixture containing `[Fact(Skip = "pending")]` is
not a skipped test result and is excluded.

All five current source files below match that gate's HEAD blobs and were absent
from its recorded full dirty status. These declarations are unchanged; this
record does not claim a fresh test execution. The historical gate wrapper exited
2 because of formatting; retaining its test outcomes does not relabel the wrapper
as successful. The separate repair/current verification evidence remains distinct.

Old skip reasons are historical descriptions, not proof that the current system
still has those defects. No defect was reproduced for this disposition exercise.

## Assigned dispositions

All test identities below have the prefix `RoslynTools.Tests.`.

| Test identity | Disposition | Required handling |
|---|---|---|
| `Architecture.CategoryInterfaceContracts.with_every_inspect_or_analyze_command_jsonoutput_is_true` | Fix | Restore a meaningful JSON admission gate for the consolidated typed preview capabilities; retired wrappers are not the target. |
| `Architecture.TestNamesReadAsEnglish.language_tool_flags_test_names_that_are_not_plain_english_sentences` | ExplicitExperimental | Future repository-development aid; no product or grammar-enforcement evidence. |
| `Mutation.ExtractMethod.replaces_tuple_return_duplicates_with_deconstruction_call` | Fix | Require fresh regression evidence, compilation, and semantic call-site checks before conditional prepare-extract-method admission. |
| `Mutation.ExtractMethod.updates_each_duplicate_file` | Fix | Require fresh regression evidence, compilation, and semantic call-site checks before conditional prepare-extract-method admission. |
| `Refactor.AddTest.command_name_determines_directory_structure` | ExplicitExperimental | Comment-only placeholder, not product evidence. Keep the accepted mixed-command pruning destination; useful scaffold intents need real tests before a future narrow recipe is admitted. |
| `Refactor.AddTest.concern_flag_adds_trait_attribute` | ExplicitExperimental | Comment-only placeholder, not product evidence. Keep the accepted mixed-command pruning destination; useful scaffold intents need real tests before a future narrow recipe is admitted. |
| `Refactor.AddTest.existing_class_adds_method_to_file` | ExplicitExperimental | Comment-only placeholder, not product evidence. Keep the accepted mixed-command pruning destination; useful scaffold intents need real tests before a future narrow recipe is admitted. |
| `Refactor.AddTest.scenario_and_outcome_generates_method_name` | ExplicitExperimental | Comment-only placeholder, not product evidence. Keep the accepted mixed-command pruning destination; useful scaffold intents need real tests before a future narrow recipe is admitted. |
| `Refactor.ExtractMethod.signature_changes_source_file_receives_new_parameters` | Fix | Require fresh regression evidence, compilation, and semantic call-site checks before conditional prepare-extract-method admission. |

Four entries require fixes before their relevant preview admission; five are
explicitly experimental and excluded from product correctness evidence.

- **JSON gate:** align the repaired gate with the final consolidated typed
  capability/profile contracts and real structured output. Do not remove the
  skip and count the existing category assertion as admission proof without
  checking the supported surface and its CLI/MCP output contract.
- **Add-test:** the [accepted disposition](surface-disposition.md) removes the
  mixed command from the public surface; a future narrow xUnit scaffold recipe
  depends on demand. These four bodies are comment-only placeholders, so
  unskipping them would produce empty passing tests. Preserve useful scenario
  intent when pruning or replacing them; do not use them as preview evidence.
- **Extraction:** the retained skip descriptions cover updated source-file
  parameters, tuple-return duplicate deconstruction, and updates to every
  duplicate file. [Conditional extraction admission](surface-disposition.md)
  requires fresh RED and useful control-flow, compilation, and semantic
  assertions. This assignment does not admit `prepare-extract-method` or prove
  extraction safe; the final whitelist may still decline it.
- **Grammar:** the future grammar-checker aid is a repository-development
  experiment, not an rt capability or a release admission check.

The known skips remain excluded from safety evidence. Fixes, removals and any
future recipe implementation follow their owning behavior-first and release
verification gates; none is accomplished by this inventory.

## Source identity and retained report provenance

| Source | SHA-256 |
|---|---|
| [tests/RoslynTools.Tests/Architecture/CategoryInterfaceContracts.cs](../../../tests/RoslynTools.Tests/Architecture/CategoryInterfaceContracts.cs) | `9f167c3cf380df7a6528702610aaacd947e242abbd472d8cc60536b9601d40ae` |
| [tests/RoslynTools.Tests/Architecture/TestNamesReadAsEnglish.cs](../../../tests/RoslynTools.Tests/Architecture/TestNamesReadAsEnglish.cs) | `40128cf13023936203616a726eb19f2e8805144dea1a29ba82b19fd5dfdb9389` |
| [tests/RoslynTools.Tests/Mutation/ExtractMethod.cs](../../../tests/RoslynTools.Tests/Mutation/ExtractMethod.cs) | `42b4c9639af17dbd90c5e1e35eaafc46b3c20ee91531af839f2d2841d323e1a6` |
| [tests/RoslynTools.Tests/Refactor/AddTest.cs](../../../tests/RoslynTools.Tests/Refactor/AddTest.cs) | `40759606f5cf5f5d17cf452524ab7b8f4487fc9b4f2e612c314245753720d373` |
| [tests/RoslynTools.Tests/Refactor/ExtractMethod.cs](../../../tests/RoslynTools.Tests/Refactor/ExtractMethod.cs) | `e11e3075b4f1e7c2886e546347aaefd03bcf9c057b2453a5ed04ee978e4b9198` |

The retained reports are identified independently of a developer-specific path:

| Report | SHA-256 | Observed skipped rows |
|---|---|---:|
| MCP Release TRX | `d86ad27316b3c12a0281ddbe876ae7fe96a0dd39cce46afc327544062fc4b3cd` | 0 |
| Core Release TRX | `bf261fe6d40d7da9fa0f3666be4d64be00b7788080e9f2312a15c9b98f124bcd` | 9 |

The machine-readable inventory and independent advisory were retained with the
implementation-session evidence. Source identity proves declaration continuity;
it does not prove a skipped assertion passed or a historical bug remains present.
The [OpenSpec tasks](tasks.md) own implementation and admission status;
[RT-016](../../../docs/issues/open/architecture/rt-016-pre-release-product-spine.md)
owns the product issue. This record adds no competing roadmap.
