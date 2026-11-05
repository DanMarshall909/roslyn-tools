# Configurable code smell review

`code-smells.json` is the editable rule catalogue for `rt analyze code-smells`
and the MCP `code_smells` tool. Host settings remain in `.rt/config.json`.
This feature is experimental and produces advisory findings, not a CI verdict.

The catalogue contains eight starter rules: large classes, long methods,
constructor dependencies, and the five SOLID principles. Each rule defines its
ID, name, category, severity, detector, thresholds, review question, accepted
exceptions, and remediation. You can edit rules, disable them with
`"enabled": false`, or append another object to `rules` with a unique ID.

## Select a catalogue and preview

The selected repository's root `code-smells.json` is used when present;
otherwise the packaged default is used. An explicit `--config` path is resolved
relative to that repository. A supplied file is a complete catalogue, not a
partial patch over the packaged default. Invalid files fail with an actionable
error rather than silently falling back.

```sh
rt analyze code-smells --solution RoslynTools.slnx --trust-repository --json
rt analyze code-smells --solution RoslynTools.slnx --config code-smells.json --rule-ids long-method --max-candidates 3 --trust-repository --json
```

Preview is the default and makes no Jev requests. Inspect `raw`, `plan`, and
`estimatedRequestCount` before adding `--transmit-source`. The API key is read
from `TYPESAFE_API_KEY`; never place it in the catalogue. Configuration cannot
grant repository trust, authorize source transmission, or change the provider
endpoint.

```sh
rt analyze code-smells --solution RoslynTools.slnx --rule-ids long-method --max-candidates 3 --transmit-source --trust-repository --json
```

The equivalent MCP arguments use `solution`, `config`, `ruleIds`,
`maxCandidates`, and `transmitSource`. Trust is established by the MCP host,
not by an argument or catalogue field. Omit `transmitSource` for preview.

## Add a semantic rule

Append this object to the existing `rules` array. `semantic` rules ask a bounded
question about each selected class or method; they do not install an executable
detector or prove an architectural violation.

```json
{
  "id": "mixed-business-policies",
  "name": "Mixed business policies",
  "category": "architecture",
  "enabled": true,
  "severity": "warning",
  "description": "A method owns independent business policies that should change separately.",
  "detection": {
    "kind": "semantic",
    "selector": "method",
    "thresholds": {}
  },
  "review": {
    "question": "Does this method own independent billing and delivery decisions, rather than coordinate separate services?",
    "acceptedExceptions": [
      {
        "id": "cohesive_orchestration",
        "description": "The method coordinates one workflow while each service owns its policy."
      }
    ],
    "evidenceGuidance": [
      "Identify the actual decisions and their reasons to change.",
      "Use uncertain when the supplied declaration cannot establish policy ownership."
    ]
  },
  "remediation": [
    "Separate independently changing policies behind explicit responsibilities.",
    "Keep orchestration together when it expresses one cohesive business outcome."
  ]
}
```

`semantic` selectors are `class` and `method`. A new rule can also reuse a
`roslyn` selector from a starter rule, retaining its supported threshold keys.
New deterministic algorithms require a code change. The exception ID
`no_accepted_exception` is reserved by the protocol; do not add it to a rule.
Evidence guidance is review guidance, not a claim that every requested fact has
been proved by the detector.

## Interpret the response

The catalogue includes `responseSchema`, the versioned response contract. Keep
it unchanged when editing rules. A response includes the effective `catalog`,
`rules`, catalogue version/hash, provider/model information, repository revision,
raw candidates, the bounded plan, findings, failures, and limitations.

Each finding identifies its rule and source location, preserves the provider's
original classification and probabilities, and reports the policy-adjusted
classification and reasons. Model confidence is not calibrated correctness.
Incomplete or conflicting evidence remains uncertain. Remediation and summaries
come from the catalogue and deterministic policy, not a fabricated model
explanation. Request IDs, resolved model, question-set version and source/evidence
hash support comparison across runs.

Comments and literal contents are sanitized before transmission, but identifiers
and type/member names remain visible. Requests are bounded; skipped and
beyond-limit candidates remain visible in the plan. Partial/failed live execution
retains its evidence and gives the CLI a nonzero exit code.

Implementation and verification evidence is tracked in
[issue #158](https://github.com/DanMarshall909/roslyn-tools/issues/158).
