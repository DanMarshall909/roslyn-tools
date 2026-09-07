# Session: 2026-09-08 — transactional slice move

**Goal:** Move a reviewed architectural slice across projects and namespaces as one
validated transaction.
**Solution:** RoslynTools.slnx

---

## Step 1 — rt move-slice [SIMULATED]

`rt refactor move-slice --type Legacy.Calculation.AssessmentService --type Legacy.Calculation.AssessmentResult --to-project WageClaims.Application --to-namespace WageClaims.Application.Calculation --analyze-dependencies --dry-run --solution WageClaims.sln`

<!-- SIMULATED — command does not exist yet -->

```json
{
  "status": "DryRun",
  "value": {
    "selection": {
      "kind": "Types",
      "types": [
        "Legacy.Calculation.AssessmentService",
        "Legacy.Calculation.AssessmentResult"
      ]
    },
    "sourceProject": "Legacy.Calculation.Application",
    "targetProject": "WageClaims.Application",
    "targetNamespace": "WageClaims.Application.Calculation",
    "applicable": true,
    "types": [
      {
        "sourceType": "Legacy.Calculation.AssessmentService",
        "targetType": "WageClaims.Application.Calculation.AssessmentService",
        "selectionReason": "Explicit"
      },
      {
        "sourceType": "Legacy.Calculation.AssessmentResult",
        "targetType": "WageClaims.Application.Calculation.AssessmentResult",
        "selectionReason": "Explicit"
      }
    ],
    "sourceEdits": [
      {
        "path": "src/backend/Calculation.Application/AssessmentService.cs",
        "description": "Change namespace Legacy.Calculation to WageClaims.Application.Calculation."
      }
    ],
    "referenceEdits": [
      {
        "path": "src/backend/Functions/CalculateClaim.cs",
        "description": "Retarget references to the moved slice."
      }
    ],
    "fileMoves": [
      {
        "sourcePath": "src/backend/Calculation.Application/AssessmentService.cs",
        "destinationPath": "src/backend/Application/Calculation/AssessmentService.cs"
      }
    ],
    "projectEdits": [],
    "requiredRelatedTypes": []
  }
}
```

The manifest is the review boundary. Apply recomputes and validates the complete
manifest, then gives the prepared source writes, file moves, and explicitly requested
project edits to the existing transactional writer.

> **[ISSUE: rt-018]** written — add a strict typed architectural slice move operation.

---

## Step 2 — reject implicit project edits [SIMULATED]

`rt refactor move-slice --namespace Legacy.Calculation --to-project WageClaims.Application --to-namespace WageClaims.Application.Calculation --dry-run --solution WageClaims.sln`

<!-- SIMULATED — the target project needs a new reference but it was not authorized -->

```json
{
  "status": "DryRun",
  "value": {
    "applicable": false,
    "projectEdits": [
      {
        "kind": "RequiredProjectReference",
        "project": "WageClaims.Functions",
        "target": "WageClaims.Application",
        "requested": false
      }
    ]
  },
  "diagnostics": [
    {
      "severity": "Error",
      "code": "project-reference-not-authorized",
      "message": "The slice requires project-reference edits. Review the manifest, then explicitly request them with a justification."
    }
  ]
}
```

An apply request with the same incomplete authorization fails before crossing the
prepared-writer boundary.

---

## Step 3 — dependency closure is advisory [SIMULATED]

`rt refactor move-slice --type Legacy.Calculation.AssessmentService --to-project WageClaims.Application --to-namespace WageClaims.Application.Calculation --analyze-dependencies --dry-run --solution WageClaims.sln`

<!-- SIMULATED — dependencies are reported, never silently selected -->

```json
{
  "requiredRelatedTypes": [
    {
      "type": "Legacy.Calculation.AssessmentPolicy",
      "project": "Legacy.Calculation.Application",
      "evidence": [
        {
          "file": "src/backend/Calculation.Application/AssessmentService.cs",
          "line": 17,
          "dependency": "Legacy.Calculation.AssessmentPolicy"
        }
      ]
    }
  ]
}
```

---

## Step 4 — MCP trust boundary [SIMULATED]

Untrusted host discovery omits `move_slice`. A host started with
`--trust-repository <absolute-root>` discovers the same typed contract as the CLI and
invokes dry-run by default. Repository paths outside that trusted root fail before
semantic loading or filesystem planning.

---

## Retrospective

### Real commands used

- Existing `move` and the prepared mutation writer established the semantic rewrite
  and transactional filesystem primitives.
- Release `MoveOperationTests` passed 15/15 at the feature baseline.

### Simulated commands → rt candidates

- `move-slice` / MCP `move_slice` — promote in this feature.

### Gaps logged

- `rt-018` records the typed operation, manifest, validation, transaction, host parity,
  and Wage Claims regression requirements.

### Next session

1. Freeze request/manifest classification tests.
2. Implement strict source selection and recursive reporting.
3. Add projected-solution validation and explicit project edits.
4. Prove CLI/MCP parity and real stdio trust behavior.

---

## Follow-up outcome: typed host adapters [REAL]

The recovered planner is now reachable through a thin `move-slice` CLI command and
the generic MCP descriptor adapter. Both hosts bind `MoveSliceRequest` and execute
the same `MoveSliceOperation`; there is no command-specific MCP handler.

Focused proof:

- 15 CLI/core `MoveSlice` tests pass, including a real `App.RunAsync` dry-run that
  emits the typed manifest and leaves disk unchanged.
- 2 MCP `MoveSlice` tests pass, covering descriptor/schema parity, startup trust
  discovery, default dry-run injection, typed invocation, and unchanged files.

Real stdio transport verification remains open; the focused MCP proof invokes the
same registry used by the stdio host but does not yet exercise framing over stdin/stdout.

---

## Follow-up outcome: fail-closed review hardening [REAL]

Review regressions reproduced and fixed three planner faults: a reciprocal pending
project-reference cycle escaped preflight, qualified type syntax could be replaced by
expression syntax, and generated former-parent imports omitted required token trivia.
Project XML loading now also enforces the resolved startup-trusted repository root.

The transaction proof injects a physical move failure after commit begins and verifies
that the source rewrite is restored and no destination remains. Wage Claims-style tests
now cover recursive/project-root mapping, former-parent imports, relative qualifiers,
multi-declaration files, external reference edits, and explicit project changes.

Trusted `move_slice` discovery and dry-run invocation now pass through the real
newline-framed `Server.RunAsync` stdin/stdout protocol path; untrusted discovery remains
omitted. Focused totals are 22 CLI/core tests and 3 MCP/protocol tests.
