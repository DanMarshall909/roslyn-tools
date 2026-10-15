# RT-022: Semantic static dependency mappings

Live requirements and workflow status belong to [issue 153](https://github.com/DanMarshall909/roslyn-tools/issues/153).
This document records design and verification evidence, not a second task list.

## Design boundary

The first slice is an internal, unwired planner. A versioned mapping identifies
the exact source overload and target method by documentation identity **and**
declaring assembly, plus an existing instance field. Selection names a project,
type and optionally individual sites. The supplied source fingerprint and mapping
preconditions bind the preparation to its inputs.

The engine owns symbol resolution, argument structure, receiver and target
binding, compilation comparison, deterministic site dispositions and no-write
candidate construction. Receiver availability, lifetime and non-null behavior,
and adapter exceptions, encoding, cancellation and resource ownership are
explicit caller-declared preconditions with contract-test evidence. Successful
compilation does not establish adapter equivalence.

The typed result wraps `PreparedChangeSet` and carries complete before/after
document text. Every selected unsupported site blocks the entire selection;
out-of-scope sites can be skipped. Empty candidates return `NoOp`. Preparation
never introduces constructors or calls the writer in the first slice.

Workspace proof consumes the existing authoritative acquisition/completeness
contract. A loaded-project count and successful compilations do not establish
scope or caller closure. The current checkout's `Partial` outline results are
navigation evidence only.

Dependency introduction must later inventory partial declarations, supported
constructor chains and every permitted caller/composition change together.
Unknown, reflected, external, DI or out-of-scope consumers block introduction.
The UTC preset uses the same engine and a characterized projection to
`GetUtcNow().UtcDateTime`; it must preserve UTC kind and evaluation count.
`DateTime.Now` remains deferred. Existing recipe consolidation and shared host
admission follow; no standalone command or new durable apply system is planned.

## Verification evidence

- Starting revision: `a45ef8b27af814a6100955bc87bb77e5d7196881`.
- Baseline: Release build, fast full gate and formatting passed. MCP: 308 passed;
  core/CLI: 4,061 passed and nine existing skips. Retained log and TRX:
  `/tmp/rt153-evidence/baseline.log` and `baseline-results/`.
- Flo runtime: source export `004f3c29752a0f8595a89b479fbf9dbd1be4b223`, built
  at `/tmp/rt153-flo-runtime-004f3c2/app/Flo.dll`; provenance retained alongside it.
- The initial contract commit provides only a compiling, unwired API scaffold
  for behavioral RED tests. It makes no implementation or readiness claim.
- The user's session parking decision is recorded in
  [the issue startup comment](https://github.com/DanMarshall909/roslyn-tools/issues/153#issuecomment-6182225045).
  Unrelated branches and the original dirty Flo cycle remain untouched.
