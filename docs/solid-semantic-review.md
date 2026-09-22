# SOLID semantic review

`rt analyze solid-review` adds an optional semantic ranking step to the deterministic
`rt analyze solid` findings. It does not replace the Roslyn analysis, prove a violation,
change source, or create a CI quality gate. The raw findings remain in every result.

## Safe workflow

Preview is the default. This loads the trusted repository, prepares bounded candidates,
and reports the symbols, byte counts, provider, model, and estimated request count. It
does not call TypeSafe or transmit source.

```powershell
rt analyze solid-review `
  --principle all `
  --max-candidates 20 `
  --solution RoslynTools.slnx `
  --trust-repository `
  --json
```

`--dry-run` is an explicit spelling of the same preview behavior. After reviewing the
plan, configure the credential in the process environment and separately opt into
transmission:

```powershell
$env:TYPESAFE_API_KEY = "<secret>"
rt analyze solid-review `
  --principle DIP `
  --max-candidates 10 `
  --transmit-source `
  --solution RoslynTools.slnx `
  --trust-repository `
  --json
```

Do not put the key in an argument, configuration file, response capture, or repository.
The command reads `TYPESAFE_API_KEY` only when a live request is about to be made.
`--dry-run` and `--transmit-source` are mutually exclusive.

For MCP, start the host with an explicitly trusted absolute repository root. The
`solid_review` tool is not advertised by an untrusted host because it can perform an
external request. An MCP payload cannot grant repository trust.

## What leaves the machine

Each eligible candidate produces one request. A request contains only:

- one candidate declaration bounded to 16 KiB;
- the selected SOLID principle, symbol, relative file path, and source line;
- principle-specific deterministic evidence bounded to 16 KiB (oversized candidates are skipped);
- whether that evidence is complete;
- the repository revision, question-set version, and source/evidence hash.

Comments, disabled preprocessor text, and directive messages are removed. String,
character, and interpolated-string contents are replaced before transmission. The
payload marks the remaining C# as untrusted application data,
and the question tells the provider not to follow instructions found inside it.
Sanitization reduces disclosure and prompt-injection risk; it does not anonymize type,
member, or identifier names. Review the preview as a data-disclosure decision.

The full repository, unrelated declarations, API key, absolute repository path, and
provider response bodies from failures are not included. There is no result cache in
V1, so neither source nor assessments are persisted by `rt` beyond output selected by
the caller. TypeSafe remains an external processor with its own current service terms
and retention policy.

## Reading the result

The JSON schema is `rt.solid-review.v1` and contains four distinct sections:

- `raw`: unchanged deterministic `solid` findings;
- `plan`: every prepared candidate, including prefiltered or incomplete items;
- `assessments`: advisory classifications and provenance for successful requests;
- `failures`: categorized per-candidate provider failures.

The three classifications are `likely_violation`, `uncertain`, and
`likely_false_positive`. They are triage labels, not calibrated proof. Question set
`solid-review-v2` keeps the relative classification but separately asks whether the
candidate shows meaningful design harm, whether an accepted exception explains it, and
which principle-specific exception applies. The assessment exposes those raw signals,
the provider's original classification, and the deterministic policy reasons behind the
displayed classification.

Code only promotes a provider label when its selected probability is at least `0.60`.
A likely violation also requires a `meaningfulDesignProblem` probability of at least
`0.60` and no substantial accepted-exception signal. A likely false positive requires
an `acceptedException` probability of at least `0.60`, no conflicting material-harm
signal, and a concrete exception reason selected at least `0.60`. Weak or contradictory
answers become `uncertain`; confidence is retained for inspection but is not treated as
correctness. These initial thresholds are conservative policy defaults, not measured
accuracy claims.

Model aliases can resolve to a different concrete version, so output records requested
and resolved models, question-set version, request ID, repository revision, and evidence
hash.

Incomplete ISP consumer/member evidence can never be displayed as a likely violation
solely because an interface is wide; it is downgraded to uncertain. The installed
`analyze-interface` empty-payload boundary is tracked in issue #136.

## Failures, cost, and drift

Authentication, validation, network, rate-limit, overload, and malformed-response
failures remain visible in a `Partial` or `Failed` envelope. Raw findings are retained,
and the CLI exits nonzero for partial or failed live execution. Rate-limit and overload
responses have bounded retries; retry exhaustion is still a failure.

The preview's `estimatedRequestCount` is the cost-control input: V1 prepares only the
first `--max-candidates` (1-100) deterministic findings and makes at most one request
per eligible prepared candidate. Later findings are reported as `beyond_limit` without
reading their source. Check current
TypeSafe pricing before a large run. A nine-candidate dogfood spike recorded on issue
#135 cost less than USD 0.01, but that observation is not a price guarantee.

Model behavior and probability calibration can drift even when the alias stays the
same. The versioned question set and resolved model make comparisons possible; rerun
evaluation fixtures before treating a changed model as equivalent. Human review should
inspect likely violations first, use `policyReasons` and
`providerFalsePositiveReason` to understand disagreements, resolve uncertain evidence,
and use likely false positives only to deprioritize—not erase—the raw candidates.

See the [V1 evaluation](evaluations/solid-semantic-review-v1.md) and
[ADR-0018](adr/0018-bounded-external-semantic-review.md).
