# Enforce rules in CI

Run a checked-in rule policy against a loadable C# solution:

```sh
rt ci check --solution App.slnx --config .rt/ci.json --json --trust-repository
```

`--config` defaults to `.rt/ci.json`. Paths are relative to the solution directory.
The root shortcut `rt check` runs the same operation. MCP exposes the typed `check`
tool; a successful tool invocation can return `passed: false`, so consumers must
inspect that field. Invalid inputs and failed checks that cannot produce a result
are MCP errors.

## Policy

A practical standard policy combines the repository architecture boundary with four
deterministic maintainability checks:

```json
{
  "version": 1,
  "rules": [
    { "id": "architecture", "config": ".rt/architecture.json" },
    { "id": "maintainability.complexity", "maxCyclomatic": 10 },
    { "id": "maintainability.large-type", "maxLines": 300 },
    { "id": "maintainability.long-method", "maxLines": 40 },
    { "id": "maintainability.too-many-dependencies", "maxParameters": 7 }
  ]
}
```

Select any non-empty combination of these rules. The architecture family uses the
existing [architecture policy](check-architecture.md), including project, semantic,
package, framework, SDK and technology restrictions. Architecture baselines label
findings as existing; they do not make a violation pass.

The maintainability rules fail only when the measured value is **greater than** the
configured maximum. A value equal to the maximum passes:

| Rule | Setting | Finding |
|---|---|---|
| `maintainability.complexity` | `maxCyclomatic` | Method cyclomatic complexity |
| `maintainability.large-type` | `maxLines` | Class or record source lines |
| `maintainability.long-method` | `maxLines` | Method source lines |
| `maintainability.too-many-dependencies` | `maxParameters` | Constructor parameter count |

Complexity uses the existing method complexity analyzer without top-N truncation.
The other maintainability rules reuse the existing `list-smells` detectors. These are
screening heuristics rather than proof that a design is wrong; tune the limits for the
repository and review the reported evidence before refactoring. Classifier-assisted
semantic smells and solution-wide inferred architecture smells are deliberately not
part of this deterministic CI policy yet.

Policies require version 1 and at least one rule. Unknown properties, unknown or
duplicate rule IDs, missing settings, mixed settings and invalid thresholds are errors.
CI policy and architecture configuration paths must stay inside the solution directory.
Configuration is data; repository evaluation still requires explicit trust.

## Results and exit codes

- Exit **0**: every configured rule ran and no findings were produced.
- Exit **1**: one or more rule violations. The full structured result remains on stdout.
- Other errors also return a **nonzero** exit status: missing/invalid configuration,
  workspace failures, empty or unsupported projects, compilation errors, or cancellation.
  Never treat the absence of findings as success unless the command completed successfully.

Results contain `passed`, `findingCount`, `checkedProjectCount`, a per-rule summary
and findings sorted by rule ID, file, line and message. JSON, YAML, LLM and text
formats use the shared result renderer. Checks use a single workspace and run in
the invoking process.

## GitHub Actions

After installing `rt`, setting up the solution's SDK and restoring dependencies:

```yaml
- name: Enforce repository rules
  run: rt ci check --solution App.slnx --json --trust-repository > rt-check.json
- name: Upload rule report
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: rt-rule-report
    path: rt-check.json
```

Use explicit trust only for repository code approved for evaluation by that
pipeline. Do not add `continue-on-error` to the enforcement step.
