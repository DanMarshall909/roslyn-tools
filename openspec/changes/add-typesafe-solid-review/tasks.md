## 1. Contract and fixtures

- [x] 1.1 Record the user journey, transmission boundary, output contract, failure
  behavior, and non-goals in OpenSpec.
- [x] 1.2 Add at least 30 labelled executable fixtures across SRP, ISP, DIP, OCP, LSP,
  accepted exceptions, and borderline cases.
- [x] 1.3 Diagnose the empty `analyze-interface` payload and either fix it or link a
  focused follow-up while retaining incomplete ISP evidence (#136).

## 2. Provider-neutral review core

- [x] 2.1 Add semantic review request/response/provider contracts and advisory ranking.
- [x] 2.2 Add bounded candidate extraction, literal/comment sanitization, hashing,
  stable ordering, revision provenance, and evidence completeness.
- [x] 2.3 Add the low-information DIP prefilter with safety tests.
- [x] 2.4 Verify the core with a fake provider; tests must not require a credential or
  network.

## 3. TypeSafe adapter

- [x] 3.1 Implement System One HTTP serialization and strict Choice response parsing.
- [x] 3.2 Read `TYPESAFE_API_KEY` only at the transmission boundary and redact all
  credential-bearing material from errors.
- [x] 3.3 Categorize authentication, rate-limit, overload, network, validation, and
  malformed-response failures.

## 4. Host surfaces and output

- [x] 4.1 Add `rt analyze solid-review` with preview-by-default, mutually exclusive
  `--dry-run`/`--transmit-source`, provider/model, candidate limit, principle,
  threshold, solution, and repository-trust behavior.
- [x] 4.2 Add the host-neutral in-process application definition for generic MCP
  projection.
- [x] 4.3 Emit the versioned raw-plus-semantic envelope and a nonzero live-review exit
  status for partial/failed provider execution.

## 5. Evaluation and documentation

- [x] 5.1 Produce a fixture evaluation comparing raw precision/recall, semantic ranking,
  and review burden without introducing a CI gate.
- [x] 5.2 Document privacy, configuration, cost, no-cache behavior, model/question drift,
  probability interpretation, and human review.
- [x] 5.3 Update the architecture overview and add an ADR for the external advisory
  provider boundary.

## 6. Verification

- [x] 6.1 Strictly validate this OpenSpec change.
- [x] 6.2 Run focused unit/integration and JSON-contract tests with the fake provider.
- [x] 6.3 Verify CLI help/dry-run and generic MCP descriptor projection.
- [ ] 6.4 Run format verification, Release build/tests, and the repository local gate.
- [x] 6.5 Run a live provider smoke test only with explicit opt-in and an available key.

## 7. False-positive diagnosis

- [x] 7.1 Decompose the broad classification into independent material-harm and
  accepted-exception signals with a typed exception reason.
- [x] 7.2 Compose weak or contradictory provider answers into `uncertain` and preserve
  the raw signals and policy reasons in output.
- [x] 7.3 Extend provider, operation, serialization, documentation, and evaluation tests.
