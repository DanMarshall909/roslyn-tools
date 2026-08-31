# Session: 2026-08-31 — Pre-release Product Spine

**Goal:** Design the smallest trustworthy `0.1-preview` workflow before committing to
the current 56-command/61-tool surface.
**Solution:** `RoslynTools.slnx`
**Tracking:** `rt-016`, `openspec/changes/shape-pre-release-product-spine/`

---

## Step 1 — Measure the current product surface [REAL]

`rt dump-help --json`

The live catalogue was parsed rather than hand-counted:

```text
commands: 56
read-only: 23
mutating: 33
entries with jsonOutput=false: 41
installed MCP tools including suggest/feedback: 61
```

This is a useful implementation inventory, but it is too broad to treat every entry as
an equal compatibility, safety, schema, and support promise for a first release.

---

## Step 2 — Ask for Git-context guidance [REAL]

`rt suggest`

Git reported 37 changed C# files in the current dirty containment branch. The command
returned:

```text
Context: 3 changed .cs file(s)
rt inspect outline "src/RoslynTools.Cli/CommandDescriptors/CommandDescriptorRegistry.cs" --solution <slnx>
rt inspect outline "src/RoslynTools.Cli/Program.cs" --solution <slnx>
rt inspect outline "src/RoslynTools.Cli/TypedCommandInvocation.cs" --solution <slnx>
rt dump-help --compact
```

The displayed limit is being reported as the total context, and the recommendations
do not classify the changed symbols, identify the mutation-containment workflow, or
offer impact and verification steps.

---

## Step 3 — Inspect a central semantic relationship [REAL]

`rt inspect find-impl RoslynTools.Core.ICommandApplicationPort --solution RoslynTools.slnx --json`

The command loaded the solution and returned 69 implementation/derived entries,
including:

```json
{
  "Symbol": "RoslynTools.Core.ICommandApplicationPort",
  "SymbolKind": "Interface",
  "ImplementationCount": 69,
  "Implementations": [
    {
      "Fqn": "RoslynTools.Core.CommandApplicationPort<TRequest, TResponse>",
      "Project": "RoslynTools",
      "Locations": [
        {
          "File": "src/RoslynTools/Core/CommandApplicationPort.cs",
          "Line": 21
        }
      ]
    },
    {
      "Fqn": "RoslynTools.Services.ExtractMethodOperation",
      "Project": "RoslynTools",
      "Locations": [
        {
          "File": "src/RoslynTools/Services/ExtractMethodOperation.cs",
          "Line": 37
        }
      ]
    }
  ]
}
```

The real response was 1,231 lines and had no visible page/continuation contract. The
semantic engine is valuable; the missing product layer is stable identities, bounds,
provenance, and a next workflow.

---

## Step 4 — Establish workspace and capability truth [SIMULATED]

`rt doctor --solution RoslynTools.slnx --format json`

<!-- SIMULATED — the product-level doctor contract does not exist yet -->

```json
{
  "schemaVersion": "rt.doctor.v1",
  "outcome": "Succeeded",
  "installation": {
    "version": "0.1.0-preview.1",
    "source": "self-contained",
    "cliLauncher": "healthy",
    "mcpLauncher": "healthy",
    "daemonRequired": false
  },
  "repository": {
    "root": ".",
    "revision": "51c293303e32ada4027c786924b94f32114cdf88",
    "workingTree": "Modified",
    "trust": "ExplicitLocalTrust"
  },
  "workspace": {
    "state": "Complete",
    "solution": "RoslynTools.slnx",
    "sdk": "10.0.201",
    "configuration": "Debug",
    "targetFrameworks": ["net10.0"],
    "loadedProjects": 4,
    "failedProjects": [],
    "diagnostics": []
  },
  "capabilities": {
    "stable": 0,
    "preview": 0,
    "experimental": 0,
    "maintainerOnly": 0
  },
  "limits": {
    "defaultItems": 100,
    "maximumOutputBytes": 1048576,
    "defaultTimeoutMs": 30000
  }
}
```

The concrete counts above are illustrative protocol fields, not measured current
workspace facts. The command must derive them from the same catalogue and workspace
provenance used by real operations.

> **[ISSUE: rt-016]** written — shape the trusted-local, structured, plan-only
> `0.1-preview` product spine.

---

## Step 5 — Discover a symbol and a workflow [SIMULATED]

`rt search-symbols ICommandApplicationPort --kind interface --solution RoslynTools.slnx --format json`

<!-- SIMULATED — intentional symbol search does not exist yet -->

```json
{
  "schemaVersion": "rt.result.v1",
  "operation": "search-symbols",
  "outcome": "Succeeded",
  "workspace": { "state": "Complete" },
  "data": {
    "total": 1,
    "items": [
      {
        "symbolId": "roslyn://RoslynTools/type/RoslynTools.Core.ICommandApplicationPort",
        "kind": "Interface",
        "fqn": "RoslynTools.Core.ICommandApplicationPort",
        "project": "RoslynTools",
        "path": "src/RoslynTools/Core/CommandApplicationPort.cs"
      }
    ]
  }
}
```

`rt suggest "change ICommandApplicationPort safely" --solution RoslynTools.slnx --format json`

<!-- SIMULATED — current suggest ranks commands rather than returning a workflow -->

```json
{
  "outcome": "Succeeded",
  "workflows": [
    {
      "id": "change-interface-contract",
      "confidence": "High",
      "steps": [
        {
          "capability": "relationships",
          "arguments": {
            "symbolId": "roslyn://RoslynTools/type/RoslynTools.Core.ICommandApplicationPort",
            "mode": "implementations"
          },
          "effects": "None",
          "reason": "Identify every implementation before changing the contract."
        },
        {
          "capability": "impact",
          "arguments": {
            "symbolId": "roslyn://RoslynTools/type/RoslynTools.Core.ICommandApplicationPort"
          },
          "effects": "None",
          "reason": "Trace consumers, projects, diagnostics, API risk, and candidate tests."
        },
        {
          "capability": "prepare-change-signature",
          "argumentsFrom": "impact.data.symbol",
          "effects": "PlanOnly",
          "reason": "Create a reviewable revision-bound plan."
        },
        {
          "capability": "verify-plan",
          "argumentsFrom": "prepare-change-signature.data.planId",
          "effects": "None",
          "reason": "Check diagnostics and semantic consequences without applying."
        }
      ],
      "executesAutomatically": false
    }
  ]
}
```

---

## Step 6 — Assess working-tree impact and introduced findings [SIMULATED]

`rt impact --from HEAD --to working-tree --solution RoslynTools.slnx --format json`

<!-- SIMULATED — first-class impact does not exist yet -->

```json
{
  "schemaVersion": "rt.impact.v1",
  "outcome": "Succeeded",
  "workspace": { "state": "Complete" },
  "data": {
    "changedSymbols": [
      "RoslynTools.Core.ApplicationCommandDefinition",
      "RoslynTools.CommandDescriptors.CommandDescriptorRegistry"
    ],
    "affectedProjects": [
      "RoslynTools",
      "rt",
      "RoslynTools.Mcp",
      "RoslynTools.Tests",
      "RoslynTools.Tests.Mcp"
    ],
    "relationshipSummary": {
      "implementations": 37,
      "callers": 12,
      "overrides": 0
    },
    "diagnosticDelta": {
      "introduced": [],
      "removed": []
    },
    "candidateTestProjects": [
      {
        "project": "RoslynTools.Tests.Mcp",
        "reason": "Command descriptor and schema consumers changed."
      }
    ],
    "limitations": [
      "Runtime reflection and external MCP clients are not fully observable from source."
    ]
  }
}
```

The counts in this simulated contract demonstrate shape only; implementation tests
must derive and pin real fixture values.

`rt check --changed --baseline artifacts/check-baseline.json --format json`

<!-- SIMULATED — current analyzers do not share one baseline-aware envelope -->

```json
{
  "schemaVersion": "rt.check.v1",
  "findings": [
    {
      "findingId": "design.too-many-dependencies:CommandDescriptorRegistry",
      "ruleId": "design.too-many-dependencies",
      "severity": "Warning",
      "confidence": "Moderate",
      "baselineState": "Introduced",
      "evidence": { "dependencyCount": 7, "threshold": 5 },
      "limitations": ["The rule is a heuristic, not a SOLID proof."]
    }
  ]
}
```

---

## Step 7 — Prepare and verify without applying [SIMULATED]

`rt change plan rename RoslynTools.Core.ICommandApplicationPort IRtApplicationPort --solution RoslynTools.slnx --format json`

<!-- SIMULATED — demonstrates the common plan contract, not a requested repository rename -->

```json
{
  "schemaVersion": "rt.change-plan.v1",
  "planId": "plan-example-001",
  "operation": "rename",
  "baseRevision": "51c293303e32ada4027c786924b94f32114cdf88",
  "inputSymbolId": "roslyn://RoslynTools/type/RoslynTools.Core.ICommandApplicationPort",
  "workspaceState": "Complete",
  "affectedPaths": [
    "src/RoslynTools/Core/CommandApplicationPort.cs",
    "src/RoslynTools.Cli/CommandDescriptors/CommandDescriptorRegistry.cs"
  ],
  "diff": "--- a/src/RoslynTools/Core/CommandApplicationPort.cs\n+++ b/src/RoslynTools/Core/CommandApplicationPort.cs\n@@\n-internal interface ICommandApplicationPort\n+internal interface IRtApplicationPort\n--- a/src/RoslynTools.Cli/CommandDescriptors/CommandDescriptorRegistry.cs\n+++ b/src/RoslynTools.Cli/CommandDescriptors/CommandDescriptorRegistry.cs\n@@\n-        IEnumerable<ICommandApplicationPort> ports,\n+        IEnumerable<IRtApplicationPort> ports,",
  "repositoryWrites": 0,
  "verification": ["diagnostics", "public-api", "affected-projects", "candidate-tests"]
}
```

`rt change verify plan-example-001 --format json`

<!-- SIMULATED — plan verification does not exist yet -->

```json
{
  "planId": "plan-example-001",
  "outcome": "Verified",
  "diagnosticDelta": { "introduced": [], "removed": [] },
  "unresolvedReferences": [],
  "affectedProjects": ["RoslynTools", "rt", "RoslynTools.Mcp"],
  "candidateTests": ["RoslynTools.Tests.Mcp"],
  "repositoryWrites": 0
}
```

The supported MCP workflow ends here. A future apply must reference these exact plan
bytes and revalidate source preconditions; it must never silently rerun rename from
the original arguments.

---

## Retrospective

### Real commands used

- `dump-help --json` measured the current public inventory.
- `suggest` exposed truncation and workflow-discovery gaps on the dirty branch.
- `find-impl` proved the semantic engine's value and the need for paging/provenance.

### Simulated commands -> product candidates

- `doctor` — release-critical workspace and capability truth.
- `search-symbols` — release-critical intentional discovery and shared resolution.
- workflow-aware `suggest` — ordered, populated, non-executing guidance.
- `impact` — highest-value composition of current semantic primitives.
- `check` plus baseline — common evidence-based findings and delta review.
- revision-bound plan plus `verify-plan` — useful change planning without unsafe apply.

### Gaps logged

- `rt-016` records the complete local product-spine programme.
- External `rt feedback add` promotion was deliberately not performed: these examples
  contain repository-derived protocol context, and this planning request did not grant
  separate authority to submit that material to an external feedback destination.

### Next session

1. Finish and verify the current P0 mutation-containment slice independently.
2. Ratify the command disposition and freeze new standalone public transformations.
3. Implement one descriptor/result-envelope pilot with `outline`.
4. Walk the real `doctor` trust/completeness contract before implementation.

### What was real and simulated

Steps 1-3 are measured current output. Steps 4-7 are contract proposals whose values
are illustrative and must become fixture-backed acceptance tests before implementation.

---

## Implementation follow-through — 2026-09-01 [REAL]

The work was implemented in value order rather than treating the original broad
catalogue as a release promise:

1. Mutation commands are local, review-first, and absent from normal MCP discovery.
2. The complete implementation inventory is frozen against an approved disposition.
3. Maintainer, trusted repository-execution, removed, and public audiences are explicit.
4. `rt.result.v1`, shared product metadata, MCP structured content, and enum-string JSON
   now provide one host-neutral product contract.
5. `doctor` reports deterministic solution/trust/workspace/capability state without
   silently evaluating an untrusted repository.
6. `search-symbols` provides deterministic identities/ranking, filters, explicit fuzzy
   matching, bounds, partial-workspace truth, and query-bound continuation.
7. `describe`, `source`, references, callers, and implementations use the same symbol
   candidate resolver; referenced-project symbols are not duplicated by consumer
   compilations.
8. `relationships` consolidates references, callers, and implementations. The unreleased
   `find-refs`, `find-callers`, and `find-impl` wrappers remain internal but are absent
   from ordinary CLI and MCP discovery.

The remaining simulated `impact`, common `check`/baseline, workflow-aware `suggest`, and
revision-bound plan/verify contracts remain open. They were not represented as complete.

## Final verification [REAL]

```text
full core/CLI: 1810 passed, 9 skipped, 0 failed
full MCP: 87 passed, 0 failed
Release solution build: passed, 0 warnings, 0 errors
dotnet format --verify-no-changes: passed
openspec validate --changes --strict --no-interactive: 2 passed
git diff --check: passed
packaged search-symbols: Succeeded, 1 candidate
packaged relationships implementations: Succeeded, 3 bounded items
ordinary inspect help: relationships/search-symbols present; find-refs absent
```

No feedback was submitted externally. No commit or push was made.
