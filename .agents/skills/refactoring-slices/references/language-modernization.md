# Detector-guided language modernization

Use this workflow when an analyser finds a repeated language-feature opportunity across a solution.

## Establish the candidate set

Run the narrow structured query and save its exact candidate inventory. For primary constructors:

```sh
rt analyze language-features \
  --solution RoslynTools.slnx \
  --feature primary_constructor \
  --refactorable-only \
  --include-locations \
  --json
```

Group candidates by architectural role, such as service, CLI adapter, MCP adapter, and test support. Select one representative pilot from each shape that could have different construction or activation behavior.

Treat the detector as an eligibility filter, not proof that the rewrite preserved runtime behavior. If source changed after discovery, rerun eligibility before applying the conversion.

## Primary-constructor safety contract

Use the conservative field-preserving form:

```csharp
internal sealed class Handler(IDependency dependency)
{
    private readonly IDependency _dependency = dependency;
}
```

- Retain readonly fields and initialize them from primary-constructor parameters. Do not replace field usages with captured parameters during the same slice; that changes storage, reflection shape, and future refactorability.
- Preserve parameter order, type, nullability, names, and the effective constructor accessibility.
- Reject constructors with validation, normalization, branching, side effects, or assignments that are not direct one-to-one parameter-to-field transfers.
- Reject or separately design cases involving multiple constructors, constructor attributes or documentation, parameter defaults or modifiers, required members, explicit base-constructor behavior, field/property initializers that depend on construction order, partial declarations, unsafe context, or parameter/member name collisions.
- When dependency injection, serializers, reflection, or framework activation constructs the type, verify the emitted constructor or exercise the real activation path. Do not infer runtime visibility solely from the compact source syntax.
- Keep type attributes and XML documentation attached to their original declaration target. Constructor-targeted metadata needs an explicit migration design.

## Apply in bounded batches

1. Convert one pilot and build before running a formatter; this exposes malformed syntax directly.
2. Run the pilot's narrow behavior or activation test.
3. Apply the proven shape to one architectural group at a time.
4. Prefer a Roslyn syntax rewrite that preserves trivia. If a generated text patch is unavoidable, derive edits from syntax locations, emit field changes in source order, and make each batch atomic so a failed context match leaves no partial rewrite.
5. Build and test after each group. A formatter is a style gate, not semantic verification.

Do not hide tool gaps with an opaque one-off rewrite. If discovery is reliable but application is missing, record the missing semantic `--dry-run`/`--apply` operation as a product gap.

## Close the loop

Rerun the same feature analysis after rebuilding the tool and solution. For a complete sweep, require the report to show no remaining eligible exceptions and the intended adoption percentage. Then run architecture tests, host-specific tests, the repository local gate, and the full solution gate as proportional to the affected layers.

Report:

- candidates discovered and converted;
- files hand-edited versus mechanically transformed;
- exclusions or exceptions left in place;
- detector adoption before and after;
- builds, focused tests, and full gates run.
