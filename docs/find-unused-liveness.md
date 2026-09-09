# Conservative liveness in `find-unused`

`find-unused` reports candidates with no references in the loaded semantic scope.
Reference absence does not prove that reflection, framework activation, or an
external caller cannot reach a member. Workspace completeness is a separate
precondition: an incomplete workspace cannot offer deletion follow-ons even when
the reference search finds nothing.

## External interface and virtual dispatch

A method is excluded from unused candidates when either of these semantic facts
holds:

- Its overridden virtual slot originates in a referenced assembly. The entire
  override chain is checked, including source-local intermediate overrides.
- It implements a method of an interface declared in a referenced assembly.
  Roslyn's interface implementation mapping must identify that actual method;
  matching a name or signature text is insufficient. This includes explicit
  implementation and constructed generic interface contracts. A source base
  method can implement the contract on a derived type even when the base type
  declares no interface. Interface mappings are collected across loaded source
  types so that inherited implementation is retained too.

These are conservative potential-entry rules, not assertions that the method ran.
An external library can invoke its own virtual or interface slots without leaving
a direct call to the implementation in source. The rules cover Roslyn visitor and
text-loader callbacks, logging scope implementations, and `TextReader` and
`TextWriter` overrides. An otherwise unused external implementation may therefore
be intentionally omitted from this inventory. Its removal requires a separate
review of the external consumer contract.

Source-local interfaces and virtual slots remain subject to semantic reference
analysis. A source project reference is local even though it has a different
  assembly identity. Ordinary overloads and sibling methods receive no exemption
merely because their containing type implements an external interface. Dead local
implementations and overrides consequently remain detectable.

Local interface calls and method-group conversions continue to count as semantic
references through the existing Roslyn reference search. This policy adds no
receiver-sensitive dataflow proof that a particular object reaches a particular
call site.

### The extraction-test symbol-resolution stub

`StubSymbolResolutionService` implements the solution-local
`ISymbolResolutionService` contract. `ExtractInterfaceService` calls
`TryFindSymbolAsync` through that contract, making the stub's implementation live
on the extraction path. The stub also declares `TryFindSymbolByFqnAsync`,
`TryFindMemberBySignatureAsync`, `FindSymbolAsync`, `FindSymbolByFqnAsync`,
`FindTypeByFqnAsync`, `FindMethodBySpecAsync`, and `FindMethodInTypeAsync`.
Those members are not invoked by this injection path; several deliberately throw
to expose an unexpected call. Their interface members have other callers in the
solution, which the existing reference search may conservatively associate with
implementations. They receive no external-dispatch exemption. A candidate does
not authorize deleting a required interface member or changing the stub's test
contract.

## Framework activation and reflection

Methods carrying `System.Runtime.CompilerServices.ModuleInitializerAttribute`
are module entry points. Both the initializer and its containing type are excluded
from unused candidates. Other methods in that type remain analyzable. A custom
attribute with the same short name does not establish module activation.

The existing Main, xUnit Fact/Theory, known command-factory, and framework-owned
type exclusions remain in place. Assembly scanning and reflective discovery keep
their existing low-confidence findings instead of becoming blanket exclusions.
External dispatch is independent of that confidence heuristic: the virtual or
interface contract itself provides the reason to retain the method.

## Regression boundary

`FindUnusedExternalDispatch` exercises metadata overrides, an intermediate local
override, Roslyn visitor callbacks, explicit and implicit external interface
implementations, overloads, dead local implementations and overrides, a local
cross-project contract, and live local interface calls.
`FindUnusedModuleInitializers` covers real module activation, container retention,
ordinary sibling candidates, and a same-named custom attribute.

This decision resolves the strategy requested by #45; the implementation and
activation regressions are tracked by #47 and #49 respectively.
