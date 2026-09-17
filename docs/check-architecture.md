# Executable architecture checks

`check-architecture` validates a repository-owned JSON policy against the loaded C# solution. It is a read-only typed operation shared by the CLI and MCP host.

Keep the policy at `.rt/architecture.json` beside the solution (the default), or pass a contained path with `--config`. Start from [the four-layer example](examples/architecture.json) and use [the version 1 JSON schema](schemas/architecture-v1.schema.json) in editors and CI.

```powershell
rt analyze check-architecture `
  --solution WageClaims.sln `
  --config .rt/architecture.json `
  --json `
  --trust-repository
```

The CLI exits `0` only when the configuration is valid, semantic analysis is complete, and no violations exist. It exits `1` when violations are reported or the operation fails. The JSON result always includes every violation and its project, repository-relative file, line, dependency, rule, source line, and stable fingerprint.

## Rules

Each loaded project must be assigned to exactly one layer or explicitly selected by `ignoredProjects`; unmatched projects are never silently ignored. Each layer selects one or more loaded projects. `allowedProjectReferences` names destination layers; no same-layer dependency is implied unless `allowSameLayerReferences` is true. `projectReferenceExceptions` permits named target projects, which supports narrow transitional exceptions such as a Functions-to-Domain reference without opening the whole layer.

Layer rules can reject namespace prefixes, exact or wildcard type names, SDKs, framework references, and NuGet packages. `technologyRules` groups the namespace, type, SDK, framework, and package signatures for a technology and declares the layers where it is forbidden. `*` and `?` wildcards are supported for project, type, SDK, framework, and package patterns; namespace patterns are prefixes, with an optional trailing `*`.

All configured project and ignore patterns must match, assigned projects must have readable project files and compile without errors, and configuration and baseline paths must remain inside the solution directory. These checks fail closed because partial workspace results could otherwise produce false compliance.

## Baselines

Baselines are optional and never suppress findings. A baseline is a JSON file containing version `1` and a `fingerprints` array copied from a reviewed result:

```json
{
  "version": 1,
  "fingerprints": ["reviewed-violation-fingerprint"]
}
```

Set `baselineFile` in the policy or pass `--baseline`. The result labels matching violations `existing` and every unmatched violation `new`, while `violations` and `violationCount` still include both. The CLI remains non-zero for either kind, and `newViolationCount` makes newly introduced debt unmistakable to CI without allowing a baseline to hide it.

The MCP tool name is `check_architecture`; it accepts `solution`, `config`, and `baseline`. An untrusted MCP host can discover this read-only operation, but semantic invocation returns RT's typed trust-required error. After review, run `rt trust add <repository>` for persistent CLI/MCP/daemon authorization, or use the compatibility `--trust-repository` scope for one CLI invocation or MCP host process.
