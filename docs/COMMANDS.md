# rt Commands

Auto-generated on every `dotnet build` from each command's static `RtCommandInfo Info` property. **Do not hand-edit.**

Total commands: **31**

## Contents

- **Navigation** — [`find-callers`](#find-callers) (callers), [`find-impl`](#find-impl) (impls), [`find-refs`](#find-refs), [`find-unused`](#find-unused) (unused), [`inspect`](#inspect), [`outline`](#outline)
- **Analysis** — [`circular-deps`](#circular-deps) (cdeps), [`dependency-graph`](#dependency-graph) (depgraph), [`list-smells`](#list-smells) (smells)
- **Mutation** — [`add-dependency`](#add-dependency) (ad), [`add-null-checks`](#add-null-checks) (anc), [`add-test`](#add-test) (at), [`convert-to-record`](#convert-to-record) (ctr), [`extract-class`](#extract-class) (ec), [`extract-interface`](#extract-interface) (ei), [`extract-method`](#extract-method) (em), [`fix-namespace`](#fix-namespace) (fns), [`implement-interface`](#implement-interface) (ii), [`inline-method`](#inline-method) (im), [`make-async`](#make-async) (ma), [`make-sealed`](#make-sealed) (ms), [`move`](#move), [`rename`](#rename) (ren), [`replace-call`](#replace-call) (rc), [`replace-datetime`](#replace-datetime), [`safe-delete`](#safe-delete) (sd)
- **Analysis — duplication** — [`find-duplicates`](#find-duplicates) (fd)
- **Navigation & inspection** — [`find-pattern`](#find-pattern) (fp)
- **Mutation — structure** — [`make-instance`](#make-instance) (mns), [`make-static`](#make-static) (mst)
- **Mutation — symbol & namespace** — [`transform-parameters`](#transform-parameters) (tp)

## Navigation

### find-callers

_Find all invocation sites for a method_

**Read-only** | Plain-text output

**Aliases:** `callers`

**When to use:**

Finding all invocation sites of a method before renaming or deleting it, or to understand the blast radius of a signature change. Unlike find-refs, this filters to true call sites only, excluding nameof() and other non-invocation references.

**Examples:**

```bash
rt find-callers --symbol SendEmail --solution C:\code\libraries\ProjectX.Libraries.sln
rt callers --symbol ProcessPayment --include-tests --solution C:\code\libraries\ProjectX.Libraries.sln
rt find-callers --symbol BuildGraphAsync --solution RoslynTools.slnx
```

---

### find-impl

_Find all implementations of an interface or abstract class (JSON)_

**Read-only** | JSON output

**Aliases:** `impls`

**When to use:**

Discovering all concrete implementations of an interface or derived classes of an abstract base, across the entire solution. Useful before making a breaking change to a contract, or to verify a new interface has been wired up everywhere expected.

**Examples:**

```bash
rt find-impl IEmailService --solution C:\code\libraries\ProjectX.Libraries.sln
rt impls ProjectX.Core.Services.IBookingRepository --solution C:\code\libraries\ProjectX.Libraries.sln
rt find-impl IRtCommand --solution RoslynTools.slnx
```

---

### find-refs

_Find all references to a symbol across the solution_

**Read-only** | Plain-text output

**When to use:**

Finding all references to any symbol (type, method, property, field, namespace) across the solution using Roslyn's semantic analysis. Prefer this over grep because it resolves overloads and type inference correctly and skips string literals/comments.

**Examples:**

```bash
rt find-refs --symbol BookingService --solution C:\code\libraries\ProjectX.Libraries.sln
rt find-refs --symbol CreateBooking --kind method --solution C:\code\libraries\ProjectX.Libraries.sln
rt find-refs --symbol IEmailService --kind type --solution RoslynTools.slnx
```

---

### find-unused

_Report types and methods with no references (dead code)_

**Read-only** | Plain-text output

**Aliases:** `unused`

**When to use:**

Identifying dead code — types and methods with zero references in the solution — before a safe-delete pass or ahead of a major refactor. Use --scope to focus on a specific namespace and filter noise from generated or test code.

**Examples:**

```bash
rt find-unused --solution C:\code\libraries\ProjectX.Libraries.sln
rt unused --scope ProjectX.Core --solution C:\code\libraries\ProjectX.Libraries.sln
rt find-unused --scope types --solution RoslynTools.slnx
```

---

### inspect

_Show type shape: members, base types, interfaces, locations (JSON)_

**Read-only** | JSON output

**When to use:**

Getting a detailed JSON description of a symbol's shape — members, base types, interfaces, constructors, locations — without opening the file. Ideal for agents that need to understand a type's surface area before planning a refactor.

**Examples:**

```bash
rt inspect BookingService --solution C:\code\libraries\ProjectX.Libraries.sln
rt inspect ProjectX.Core.Services.IEmailService --kind type --solution C:\code\libraries\ProjectX.Libraries.sln
rt inspect IRtCommand --solution RoslynTools.slnx
```

---

### outline

_List all types and member signatures in a file or namespace (JSON)_

**Read-only** | JSON output

**When to use:**

Getting a JSON summary of all types and member signatures in a file or namespace without reading the raw source. Useful for agents to orient themselves in an unfamiliar module before planning a refactor, or to diff the public surface area across branches.

**Examples:**

```bash
rt outline src/backend/TourStore.Api/Controllers/Admin/AdminController.cs --solution C:\code\libraries\ProjectX.Libraries.sln
rt outline ProjectX.Core.Services --solution C:\code\libraries\ProjectX.Libraries.sln
rt outline RoslynTools.Commands --solution RoslynTools.slnx
```

---

## Analysis

### circular-deps

_Detect circular namespace/type dependencies in the solution_

**Read-only** | Plain-text output

**Aliases:** `cdeps`

**When to use:**

Detecting circular namespace dependencies in a solution before a refactor, or after adding new inter-namespace references. Use --scope to limit analysis to your domain namespaces and ignore third-party noise.

**Examples:**

```bash
rt circular-deps --solution C:\code\libraries\ProjectX.Libraries.sln
rt cdeps --scope ProjectX.Core --solution C:\code\libraries\ProjectX.Libraries.sln
rt circular-deps --solution RoslynTools.slnx --scope RoslynTools
```

---

### dependency-graph

_Build a type-level dependency graph for the solution_

**Read-only** | JSON output

**Aliases:** `depgraph`

**When to use:**

Visualising the type-level dependency graph for a solution or a specific root type, to understand coupling before a large refactor. Use --format dot to pipe into Graphviz, or --format json for programmatic consumption. Use --type + --depth to focus on a single type's fan-out.

**Examples:**

```bash
rt dependency-graph --solution C:\code\libraries\ProjectX.Libraries.sln --scope ProjectX.Core
rt depgraph --type BookingService --depth 2 --solution RoslynTools.slnx
rt dependency-graph --format dot --solution RoslynTools.slnx | dot -Tsvg -o deps.svg
```

**Known issues:**

- Graph edges are built from fields, method parameters/returns, base types, and interfaces only — local variable type usages inside method bodies are missed. Static command classes with no fields appear as isolated nodes even when they reference WorkspaceLoader/SolutionWriter internally. See ai-improvements row #12.

---

### list-smells

_Report code smells: large classes, long methods, over-injected constructors_

**Read-only** | Plain-text output

**Aliases:** `smells`

**When to use:**

Getting a prioritised list of code smells — oversized classes, long methods, and constructors with too many parameters — to guide a refactor session. Use --scope to focus on your domain code and skip generated or test noise.

**Examples:**

```bash
rt list-smells --solution C:\code\libraries\ProjectX.Libraries.sln
rt smells --scope ProjectX.Core --max-class-lines 200 --max-method-lines 30 --solution C:\code\libraries\ProjectX.Libraries.sln
rt list-smells --solution RoslynTools.slnx --max-params 5
```

---

## Mutation

### add-dependency

_Inject a constructor dependency into a class_

**Mutation** (writes files) | Plain-text output

**Aliases:** `ad`

**When to use:**

Injecting a new constructor dependency into a class without manually editing the constructor and field declarations. Prefer this over a manual edit when the class has multiple constructors or when you want to be confident the field naming convention (_camelCase) is applied consistently.

**Examples:**

```bash
rt add-dependency --type BookingService --dependency IEmailService --solution C:\code\libraries\ProjectX.Libraries.sln
rt ad --type OrderController --dependency IPaymentGateway --field _payment --dry-run
rt add-dependency --type ReportGenerator --dependency IRepository --solution RoslynTools.slnx
```

---

### add-null-checks

_Insert ArgumentNullException.ThrowIfNull guards for reference-type parameters_

**Mutation** (writes files) | Plain-text output

**Aliases:** `anc`

**When to use:**

Retrofitting ArgumentNullException.ThrowIfNull guards onto reference-type parameters in an existing method. Prefer this over a manual edit when a method has several parameters and you want to skip any that are already guarded or are nullable (annotated with ?).

**Examples:**

```bash
rt add-null-checks --type BookingService --method CreateBooking --solution C:\code\libraries\ProjectX.Libraries.sln
rt anc --type OrderProcessor --method Process --dry-run
rt add-null-checks --type ReportController --method Generate --solution RoslynTools.slnx
```

---

### add-test

_Append xUnit test stubs to an existing test class_

**Mutation** (writes files) | Plain-text output

**Aliases:** `at`

**When to use:**

Appending xUnit [Fact] stub methods to an existing test class — either a single named test or a batch from semicolon-separated acceptance criteria. Eliminates the boilerplate of writing throw-stub test shells by hand and optionally groups ACs into a nested class.

**Examples:**

```bash
rt add-test --class BookingServiceTests --name Returns_404_When_Not_Found --solution RoslynTools.slnx
rt at --class OrderTests --acs "Returns total;Applies discount;Rejects empty cart" --nested-class WhenCalculatingTotal
rt add-test --class PaymentTests --acs "Charges card;Sends receipt" --file tests/PaymentTests.cs --dry-run
```

---

### convert-to-record

_Convert a class to a record type with positional parameters_

**Mutation** (writes files) | Plain-text output

**Aliases:** `ctr`

**When to use:**

Converting a class with init-only properties to a positional record, automatically removing redundant Equals/GetHashCode overrides and == operators that the record synthesises. Best suited to immutable DTOs and value objects.

**Examples:**

```bash
rt convert-to-record --type BookingRequest --solution C:\code\libraries\ProjectX.Libraries.sln
rt ctr --type Money --dry-run
rt convert-to-record --type ProductDto --solution RoslynTools.slnx
```

---

### extract-class

_Move named members from a class into a new class file_

**Mutation** (writes files) | Plain-text output

**Aliases:** `ec`

**When to use:**

Moving a named set of members out of an oversized class into a new dedicated class file. When --static is not set, the command also wires the new class into the source class as a constructor-injected dependency, making it immediately usable without manual plumbing.

**Examples:**

```bash
rt extract-class --type AdminController --members ExportCsv,ImportCsv --new-type CsvController --solution C:\code\libraries\ProjectX.Libraries.sln
rt ec --type ReportService --members BuildReport,FormatReport --new-type ReportFormatter --static --dry-run
rt extract-class --type ProductService --members ValidateProduct --new-type ProductValidator --solution RoslynTools.slnx --output-dir src/Validators
```

---

### extract-interface

_Generate IXxx interface from a class's public members_

**Mutation** (writes files) | Plain-text output

**Aliases:** `ei`

**When to use:**

Generating an IXxx interface from an existing class's public members and updating the class declaration to implement it. Use this before injecting a service as a dependency, or when introducing a seam for testing.

**Examples:**

```bash
rt extract-interface --type BookingService --solution C:\code\libraries\ProjectX.Libraries.sln
rt ei --type EmailSender --output-dir src/Interfaces --dry-run
rt extract-interface --type HeroApiClient --namespace ProjectX.Core.Abstractions --solution RoslynTools.slnx
```

---

### extract-method

_Extract a range of statements into a new private method._

**Mutation** (writes files) | Plain-text output

**Aliases:** `em`

**When to use:**

Pulling a consecutive range of statements out of a long method body into a new private method. Specify the line range from the source file; the command infers parameter and return types from context. Use --after/--before to control insertion point.

**Examples:**

```bash
rt extract-method src/Services/BookingService.cs ValidateBooking --start-line 42 --end-line 58 --solution C:\code\libraries\ProjectX.Libraries.sln
rt em src/Controllers/AdminController.cs BuildCsvRow --start-line 110 --end-line 125 --dry-run
rt extract-method src/RoslynTools/Commands/RenameCommand.cs BuildOptions --start-line 30 --end-line 45 --after Build --solution RoslynTools.slnx
```

---

### fix-namespace

_Align a file's namespace declaration to its folder path_

**Mutation** (writes files) | Plain-text output

**Aliases:** `fns`

**When to use:**

Correcting a file's namespace declaration to match its folder path after moving files, or when a file was created in the wrong location. Also updates any using directives in other files that imported the old namespace.

**Examples:**

```bash
rt fix-namespace --file src/Services/BookingService.cs --solution C:\code\libraries\ProjectX.Libraries.sln
rt fns --file src/RoslynTools/Commands/RenameCommand.cs --dry-run
rt fix-namespace --file src/Domain/Order.cs --solution RoslynTools.slnx
```

---

### implement-interface

_Generate throw-stub implementations for unimplemented interface members_

**Mutation** (writes files) | Plain-text output

**Aliases:** `ii`

**When to use:**

Generating throw-stub implementations for all unimplemented interface members in a class. Use after extract-interface or when a class is added to an interface's base list and needs skeleton stubs before real implementations are written.

**Examples:**

```bash
rt implement-interface --type BookingRepository --interface IBookingRepository --solution C:\code\libraries\ProjectX.Libraries.sln
rt ii --type EmailSender --dry-run
rt implement-interface --type HeroApiClient --solution RoslynTools.slnx
```

---

### inline-method

_Inline a method by replacing all call sites with the method body_

**Mutation** (writes files) | Plain-text output

**Aliases:** `im`

**When to use:**

Inlining a trivial helper method (expression-bodied or single-return block) at every call site and removing the method declaration. Best for one-liner wrappers that add no clarity — over-extracted private methods that obscure intent.

**Examples:**

```bash
rt inline-method --type MathHelper --method Double --solution RoslynTools.slnx
rt im --type OrderService --method GetTotal --dry-run --solution C:\code\libraries\ProjectX.Libraries.sln
```

**Known issues:**

- Method removal after inlining matches by identifier text only — removes the wrong method when the same name exists in a different class in the same file. Fix: scope to containing TypeDeclarationSyntax by type name and parameter count. See ai-improvements row #8.
- ParameterSubstitutor does not parenthesise substituted arguments — `Double(a+b)` inlines as `a+b*2` instead of `(a+b)*2`. Fix: wrap non-trivial argument expressions in ParenthesizedExpressionSyntax. See ai-improvements row #9.

---

### make-async

_Convert a synchronous method to async Task<T> and update direct callers_

**Mutation** (writes files) | Plain-text output

**Aliases:** `ma`

**When to use:**

Converting a synchronous method to async Task<T> and updating its direct call sites with await, without manually hunting every invocation. Use --no-rename to skip the Async suffix when the call sites already have naming that makes async clear.

**Examples:**

```bash
rt make-async --type BookingService --method Save --solution C:\code\libraries\ProjectX.Libraries.sln
rt ma --type EmailSender --method Send --no-rename --dry-run
rt make-async --type HeroApiClient --method FetchAvailability --solution RoslynTools.slnx
```

---

### make-sealed

_Add the sealed modifier to a class that has no derived types_

**Mutation** (writes files) | Plain-text output

**Aliases:** `ms`

**When to use:**

Adding the sealed modifier to a class that has no derived types in the solution. Sealing classes improves runtime dispatch performance and communicates design intent. The command verifies no derived classes exist before modifying, making it safe to run without a prior manual check.

**Examples:**

```bash
rt make-sealed --type BookingValidator --solution C:\code\libraries\ProjectX.Libraries.sln
rt ms --type CsvFormatter --dry-run
rt make-sealed --type RtCommandInfo --solution RoslynTools.slnx
```

---

### move

_Move a type to a different namespace, updating the declaration and all references_

**Mutation** (writes files) | Plain-text output

**When to use:**

Moving a type to a different namespace — or batch-moving all types in a namespace — while updating the declaration and all reference sites (using directives and fully-qualified names). Use --move-file to also relocate the source file(s) to match the new namespace folder structure.

**Examples:**

```bash
rt move --type ProjectX.Core.Services.BookingService --to-namespace ProjectX.Domain.Services --solution C:\code\libraries\ProjectX.Libraries.sln
rt move --namespace ProjectX.Core.Models --to-namespace ProjectX.Domain.Entities --move-file --dry-run --solution C:\code\libraries\ProjectX.Libraries.sln
rt move --type RoslynTools.Commands.RenameCommand --to-namespace RoslynTools.Commands.Rename --solution RoslynTools.slnx
```

---

### rename

_Rename a symbol and all its references across the solution_

**Mutation** (writes files) | Plain-text output

**Aliases:** `ren`

**When to use:**

Renaming a C# symbol (type, method, property, field, namespace) and updating every reference across the solution. Prefer over grep+sed: rt uses Roslyn semantic rename so references that only bind by type inference are also updated, and string literals/comments are not accidentally rewritten. Use --csv to batch-apply a curated rename mapping file.

**Examples:**

```bash
rt rename OldTypeName NewTypeName --solution MySolution.sln
rt rename MyNs.MyType.OldProp NewProp --kind property --dry-run
rt rename --csv scripts/test-renames.csv --solution RoslynTools.slnx --dry-run
rt ren FooService BarService              # short alias, auto-detect solution
```

---

### replace-call

_Replace all invocations of one static method with another_

**Mutation** (writes files) | Plain-text output

**Aliases:** `rc`

**When to use:**

Replacing all invocations of a static method with a different method name across the solution. Use when renaming a utility method that isn't captured by a semantic rename (e.g. because callers use it via a static import or the old name needs to stay as an overload).

**Examples:**

```bash
rt replace-call --from Logger.Log --to LogInfo --solution C:\code\libraries\ProjectX.Libraries.sln
rt rc --from DateHelper.UtcNow --to TimeProvider.GetUtcNow --dry-run --solution C:\code\libraries\ProjectX.Libraries.sln
rt replace-call --from Console.WriteLine --to Logger.Info --solution RoslynTools.slnx
```

---

### replace-datetime

_Replace DateTime.UtcNow / DateTime.Now with injected TimeProvider, adding constructor injection_

**Mutation** (writes files) | Plain-text output

**When to use:**

Replacing all DateTime.UtcNow and DateTime.Now usages with injected TimeProvider calls (_timeProvider.GetUtcNow().UtcDateTime / GetLocalNow().DateTime) and automatically wiring TimeProvider into the affected class constructors. Run solution-wide before adding unit tests that need controllable time.

**Examples:**

```bash
rt replace-datetime --solution C:\code\libraries\ProjectX.Libraries.sln
rt replace-datetime --dry-run --solution C:\code\libraries\ProjectX.Libraries.sln
rt replace-datetime --solution RoslynTools.slnx
```

---

### safe-delete

_Delete a type or member only if it has no references_

**Mutation** (writes files) | Plain-text output

**Aliases:** `sd`

**When to use:**

Deleting a type or member only after verifying it has zero references in the solution. Safer than a manual delete because the command checks references (excluding nameof() and the declaration itself) before touching any file, and exits non-zero if references exist.

**Examples:**

```bash
rt safe-delete --symbol OrphanedHelper --solution C:\code\libraries\ProjectX.Libraries.sln
rt sd --symbol BookingService.LegacyMethod --kind method --dry-run --solution C:\code\libraries\ProjectX.Libraries.sln
rt safe-delete --symbol OldDtoType --kind type --solution RoslynTools.slnx
```

---

## Analysis — duplication

### find-duplicates

_Report exact-clone code blocks repeated across the solution_

**Read-only** | JSON output

**Aliases:** `fd`

**When to use:**

Discovering repeated code blocks (exact clones) across a solution before an extract-method pass. Use --min-statements to tune noise vs. signal, --min-occurrences to target the most-duplicated blocks, and --scope to focus on one subsystem.

**Examples:**

```bash
rt find-duplicates --solution C:\code\libraries\ProjectX.Libraries.sln
rt fd --min-statements 5 --solution C:\code\libraries\ProjectX.Libraries.sln
rt find-duplicates --min-statements 4 --min-occurrences 3 --solution RoslynTools.slnx
rt find-duplicates --scope MyApp.Services --solution RoslynTools.slnx
rt find-duplicates --include-tests --solution RoslynTools.slnx
rt find-duplicates --max-statements 20 --solution RoslynTools.slnx
rt find-duplicates --json --solution RoslynTools.slnx
```

---

## Navigation & inspection

### find-pattern

_Find all solution locations structurally matching a given statement range._

**Read-only** | JSON output

**Aliases:** `fp`

**When to use:**

Find every location in the solution that structurally matches a given line range — same SyntaxKind tree shape, only identifier and literal values differ. Useful for spotting duplicated logic even when variable names differ.

**Examples:**

```bash
rt find-pattern --file src/Services/BookingService.cs --start-line 42 --end-line 50 --solution RoslynTools.slnx
rt fp --file src/Controllers/AdminController.cs --start-line 110 --end-line 125 --scope RoslynTools.Core --solution RoslynTools.slnx
```

---

## Mutation — structure

### make-instance

_Remove static from a method and rewrite call sites to use an instance_

**Mutation** (writes files) | Plain-text output

**Aliases:** `mns`

**When to use:**

Converting a static method to an instance method. Removes the `static` modifier from the declaration and rewrites `Type.Method(...)` call sites to `instance.Method(...)` where an instance is reachable (local variable, field, or parameter of the correct type). Call sites with no reachable instance are warned and left unchanged.

**Examples:**

```bash
rt make-instance --method OrderService.Validate --solution C:\code\libraries\ProjectX.Libraries.sln
rt mns --method MathHelper.Compute --dry-run --solution RoslynTools.slnx
```

---

### make-static

_Convert an instance method to static when it has no instance-state dependencies_

**Mutation** (writes files) | Plain-text output

**Aliases:** `mst`

**When to use:**

Converting an instance method to static when it has no instance-state dependencies. Validates safety (no 'this', no instance field/property access, not virtual/override/abstract) before modifying and rewrites all call sites from instance.Method() to Type.Method().

**Examples:**

```bash
rt make-static --method OrderValidator.Validate --solution C:\code\libraries\ProjectX.Libraries.sln
rt mst --method MathHelper.Clamp --dry-run
rt make-static --method SymbolLocator.ParseSpec --solution RoslynTools.slnx
```

---

## Mutation — symbol & namespace

### transform-parameters

_Extract method parameters into a new record and update all call sites._

**Mutation** (writes files) | Plain-text output

**Aliases:** `tp`

**When to use:**

Introduce a Parameter Object: extract a method's parameters into a new record and update all call sites. Useful when a method has 3+ parameters that logically belong together.

**Examples:**

```bash
rt transform-parameters --method OrderService.PlaceOrder --new-type PlaceOrderArgs --solution MySolution.sln
rt tp --method BookingService.Create --new-type CreateBookingArgs --dry-run
rt transform-parameters --method Calculator.Add --existing-type MyApp.AddArgs --solution RoslynTools.slnx --dry-run
```

---

