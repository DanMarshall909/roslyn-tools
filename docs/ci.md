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

```json
{
  "version": 1,
  "rules": [
    { "id": "architecture", "config": ".rt/architecture.json" },
    { "id": "maintainability.complexity", "maxCyclomatic": 10 }
  ]
}
```

Select either family or both. The architecture family uses the existing
[architecture policy](check-architecture.md), including project, semantic, package,
framework, SDK and technology restrictions. Architecture baselines label findings
as existing; they do not make a violation pass.

Complexity fails when a method's cyclomatic complexity is **greater than** the
configured maximum. A value equal to the maximum passes. It uses the existing
method complexity analyzer without top-N truncation; generated code is excluded
according to that analyzer's existing rules. This first version does not support
custom scripts, severity overrides, rule plugins or suppressions.

Policies require version 1 and at least one rule. Unknown properties, unknown or
duplicate rule IDs, missing settings and invalid thresholds are errors. CI policy
and architecture configuration paths must stay inside the solution directory.
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
