# PRD: Eight New Refactoring Commands

## Introduction

Extend the `rt` CLI with eight new Roslyn-based refactoring and analysis commands targeting the ProjectX codebase. ProjectX has 247+ interfaces, 348+ service files, and a strict DI/repository/service pattern — these commands automate the most repetitive structural work developers face when navigating and evolving that codebase.

All commands follow the established conventions: `Commands/` (CLI definition) -> `Core/` (Roslyn logic) -> `Models/`; diagnostics to stderr; `--dry-run` supported on mutation commands; xUnit tests written first.

---

## Goals

- Automate interface extraction so every new service gets its `IXxx` in seconds
- Catch dead code and code smells without opening Visual Studio
- Make structural refactors (extract class, add dependency, fix namespace) safe and repeatable
- Provide async propagation without manual chain-editing
- Give callers a focused call-site view without sifting through all reference types

---

## Implementation Order

Commands are grouped by complexity and dependency. Complete each phase before starting the next.

**Phase 0 — Prerequisites**

- US-00A: Upgrade `SolutionWriter` to support new file creation ✓

**Phase 1 — Read-only analysis (no mutation, no file creation)**

- US-007: `find-callers`
- US-008: `list-smells`

**Phase 2 — Mutation commands that reuse existing core**

- US-001: `extract-interface`
- US-002: `fix-namespace`

**Phase 3 — Mutation + full-solution scan**

- US-005: `add-dependency`
- US-003: `find-unused`

**Phase 4 — Most complex / highest risk**

- US-004: `extract-class`
- US-006: `make-async`

---

## User Stories

### US-00A: Upgrade `SolutionWriter` to support new file creation ✓

**Description:** As a developer, I need `SolutionWriter.ApplyChangesAsync` to create new files (not just update existing ones) so that `extract-interface` and `extract-class` can write their output.

**Acceptance Criteria:**
- [x] `SolutionWriter.ApplyChangesAsync` handles `GetAddedDocuments()` by writing the new file to disk (respecting `--dry-run`)
- [x] Dry-run prints the full content of new files with a clear `+++ (new file)` header
- [x] xUnit test: solution with an added document writes the file; dry-run does not
- [x] Existing update and diff behaviour unchanged (no regressions)
- [x] `dotnet build` and `dotnet format --verify-no-changes` pass

---

### US-001: `extract-interface` — generate IXxx from a class
**Description:** As a developer, I want to generate a matching interface from a class's public members so I can follow the ProjectX IXxx convention without writing boilerplate by hand.

**Aliases:** `ei`

**Acceptance Criteria:**
- [ ] `rt extract-interface --type AgentService --solution ...` creates `IAgentService.cs` alongside `AgentService.cs` (or in specified dir if `--output-dir` supplied)
- [ ] Interface contains all `public` methods and properties (no fields, no private/internal)
- [ ] Generated interface namespace matches the class namespace (or `--namespace` override)
- [ ] Class declaration updated to `: IAgentService` (or appended if already implementing others)
- [ ] `--dry-run` prints unified diff; no files written
- [ ] xUnit tests cover: method-only class, property+method class, class already implementing another interface, `--dry-run` produces no file changes
- [ ] `dotnet build` and `dotnet format --verify-no-changes` pass

---

### US-002: `fix-namespace` — align namespace to folder path
**Description:** As a developer, I want to fix a file's namespace to match its folder structure so the codebase stays navigable as files are moved.

**Aliases:** `fns`

**Implementation note:** This is a thin wrapper around `MoveCommand`'s single-type namespace rewriting logic. The new part is auto-detecting the target namespace from the file's folder path relative to the project root. Reuse `MoveCommand.ExecuteSingleTypeMoveAsync` or extract its core into a shared helper.

**Acceptance Criteria:**
- [ ] `rt fix-namespace --file path/to/File.cs --solution ...` computes expected namespace from folder path relative to the project root and its `RootNamespace`
- [ ] Updates the `namespace` declaration in the file
- [ ] Updates all `using` statements in other files that reference the old namespace (reuses MoveCommand's reference-update logic)
- [ ] No-op with clear message when namespace already matches
- [ ] `--dry-run` prints what would change; no writes
- [ ] xUnit tests: file already correct (no-op), file one level wrong, file deeply nested, `--dry-run`
- [ ] `dotnet build` and `dotnet format --verify-no-changes` pass

---

### US-003: `find-unused` — dead code report
**Description:** As a developer, I want to find types and methods with no references so I can safely delete dead code.

**Aliases:** `unused`

**Acceptance Criteria:**
- [ ] `rt find-unused --solution ...` scans the solution and reports all `public`/`internal` types and methods with zero references
- [ ] Excludes: entry points (`Main`), types decorated with serialisation attributes (`[Serializable]`, `[DataContract]`, `[JsonObject]`, etc.), test classes/methods, interface members (reported via implementing class instead)
- [ ] `--scope` flag accepts `types`, `methods`, or `all` (default `all`)
- [ ] Progress reported to stderr (solution may have ~30 projects)
- [ ] Read-only — no `--dry-run` needed; never modifies files
- [ ] xUnit tests: fixture with known unused method, fixture where everything is referenced, exclusion of serialisation-attributed types
- [ ] `dotnet build` and `dotnet format --verify-no-changes` pass

---

### US-004: `extract-class` — pull members into a new class
**Description:** As a developer, I want to move a named group of members from a class into a new class file so I can split large classes without manual copy-paste.

**Aliases:** `ec`

**Acceptance Criteria:**
- [ ] `rt extract-class --type BigService --members "DoFoo,DoBar,_fooField" --new-type FooHelper --solution ...`
- [ ] New class file created alongside the source file (or `--output-dir`)
- [ ] Named members removed from source class
- [ ] By default, source class receives a private field of the new type + constructor injection. `--static` flag makes the new class static with static methods instead (no injection)
- [ ] Namespace of new class matches source class namespace
- [ ] `--dry-run` prints diff for both files, creates nothing
- [ ] xUnit tests: extract one method, extract field + two methods, `--static` flag, member not found reports clear error, `--dry-run`
- [ ] `dotnet build` and `dotnet format --verify-no-changes` pass

---

### US-005: `add-dependency` — inject a new constructor parameter
**Description:** As a developer, I want to add a constructor dependency to a class (parameter + backing field) so I don't hand-edit constructor signatures and SimpleInjector registrations.

**Aliases:** `ad`

**Acceptance Criteria:**
- [ ] `rt add-dependency --type AgentService --dependency IEmailService --solution ...`
- [ ] Field name auto-derived by convention: `IEmailService` -> `_emailService`. Override with `--field <name>`
- [ ] Adds `private readonly IEmailService _emailService;` field
- [ ] Adds `IEmailService emailService` parameter to the primary constructor (last position)
- [ ] Adds `_emailService = emailService;` assignment in constructor body
- [ ] If class has no constructor, generates one
- [ ] `--register` flag appends `container.Register<IEmailService, EmailService>(Lifestyle.Scoped);` to `SimpleInjector.cs`. Concrete type auto-derived by stripping leading `I`; override with `--concrete <type>`. Warns if already registered
- [ ] `--dry-run` prints diff; no writes
- [ ] xUnit tests: class with no constructor (generates one), class with existing constructor, `--register` flag, `--concrete` override, type not found error
- [ ] `dotnet build` and `dotnet format --verify-no-changes` pass

---

### US-006: `make-async` — convert sync method to async
**Description:** As a developer, I want to convert a synchronous method to `async Task<T>` and have direct callers updated so I don't break the call chain manually.

**Aliases:** `ma`

**Acceptance Criteria:**
- [ ] `rt make-async --type AgentService --method GetAgent --solution ...`
- [ ] Return type `T` -> `Task<T>`, `void` -> `Task`
- [ ] Method renamed to `GetAgentAsync` unless `--no-rename` supplied
- [ ] `await` inserted before any `Task`-returning calls inside the body
- [ ] **Direct callers only** (one level): each call site updated to `await` the result; if the calling method is not already async, it is marked `async` and its return type updated. Propagation stops there — no transitive walk
- [ ] C# 7.x compatible output (no `IAsyncEnumerable`, no `ValueTask` unless `--value-task`)
- [ ] `--dry-run` shows diff across all affected files
- [ ] xUnit tests: already-async method (no-op), void method, method with multiple return paths, caller not already async gets updated, caller already async just gets `await` added
- [ ] `dotnet build` and `dotnet format --verify-no-changes` pass

---

### US-007: `find-callers` — list call sites for a method
**Description:** As a developer, I want a focused list of where a specific method is invoked so I can assess impact before changing its signature.

**Aliases:** `callers`

**Acceptance Criteria:**
- [ ] `rt find-callers --symbol "AgentService.GetAgent" --solution ...`
- [ ] Returns only method invocation sites (not property reads, not type references, not `nameof()` — unlike `find-refs`)
- [ ] Each result includes file path, line number, containing method name, and the source line text
- [ ] `--include-tests` flag includes test project results (excluded by default)
- [ ] Read-only — no `--dry-run` needed
- [ ] xUnit tests: method with multiple callers, method with 0 callers, overloaded method (disambiguate via SymbolLocator), test exclusion flag
- [ ] `dotnet build` and `dotnet format --verify-no-changes` pass

---

### US-008: `list-smells` — code smell report
**Description:** As a developer, I want a report of oversized classes, long methods, and over-injected constructors so I can prioritise refactoring effort.

**Aliases:** `smells`

**Acceptance Criteria:**
- [ ] `rt list-smells --solution ...` reports three smell categories:
  - **LargeClass:** types with line count > `--max-class-lines` (default 300)
  - **LongMethod:** methods with line count > `--max-method-lines` (default 40)
  - **TooManyDependencies:** constructors with parameter count > `--max-params` (default 7)
- [ ] All thresholds configurable via flags
- [ ] Each entry includes: name, file, line number, actual count
- [ ] `--scope` flag to target a single project or namespace prefix
- [ ] Read-only — never modifies files
- [ ] xUnit tests: fixture with known violations, all-clean fixture returns empty results, `--scope` filters correctly
- [ ] `dotnet build` and `dotnet format --verify-no-changes` pass

---

## Functional Requirements

- FR-1: All 8 commands registered in `App.RunAsync` and listed in `--help`
- FR-2: Each command follows `Commands/` -> `Core/` -> `Models/` layering. Commands define CLI args and delegate to Core for Roslyn logic. Commands may reference Roslyn types (e.g. `Solution`) for plumbing — this matches the existing pattern
- FR-3: All commands that modify files support `--dry-run` (unified diff output, zero file writes)
- FR-4: All commands emit diagnostics/progress to stderr. Output format to stdout matches the existing plain-text convention (file paths, line numbers, diffs). JSON output is a future standardisation effort, not in scope here
- FR-5: Workspace loading reuses `WorkspaceLoader`; symbol lookup reuses `SymbolLocator` where applicable
- FR-6: xUnit test project (`RoslynTools.Tests`) with a C# 7.3 fixture solution; each US has at least the listed test cases written before implementation begins (TDD)
- FR-7: `dotnet build` and `dotnet format --verify-no-changes` must pass after each command is added
- FR-8: Commands that accept a `--type` or `--symbol` flag use `SymbolLocator`'s interactive disambiguation when multiple matches exist
- FR-9: Code-generating commands (`extract-interface`, `extract-class`, `add-dependency`, `make-async`) must produce C# 7.x compatible output (no C# 8+ syntax). The `rt` tool itself uses C# 11+
- FR-10: Each command has a short alias (specified per story)

---

## Non-Goals

- No Visual Studio / Rider plugin — CLI only
- No automatic git commits after refactoring
- `make-async` propagation is direct callers only — no transitive closure
- `add-dependency --register` does not auto-discover the concrete type from the solution — developer supplies it (or accepts the `I`-stripping convention)
- No project scaffolding (new service/repo boilerplate) — that is a separate future tool
- No support for VB.NET or F# files
- No JSON output standardisation (future effort across all commands)

---

## Technical Considerations

- `MSBuildLocator.Register()` must fire before any `MSBuildWorkspace` type is loaded — all commands go through `App.RunAsync`, so this is already handled
- `SolutionWriter` currently prints a WARNING and skips added documents (line 42-44). US-00A upgrades it to create new files, which unblocks `extract-interface` and `extract-class`
- `fix-namespace` shares most logic with `MoveCommand`'s single-type mode. Extract shared helpers or call MoveCommand's core directly rather than reimplementing namespace rewriting
- `extract-interface` and `extract-class` use `SyntaxEditor` / `DocumentEditor` from `Microsoft.CodeAnalysis.Editing`
- `make-async` uses `Microsoft.CodeAnalysis.CSharp.Syntax` async rewriting; test fixture must enforce `LangVersion 7.3` to catch C# 8+ leakage
- `find-unused` must load all projects in the solution to get cross-project reference counts — may be slow on ProjectX (~30 projects); report progress to stderr
- Integration tests that load the fixture `.slnx` are preferred over heavy mocking (per existing team feedback)

---

## Success Metrics

- Each command completes on the full ProjectX Libraries solution in < 60 seconds
- `extract-interface` on a ProjectX service produces a compilable interface with no manual fixup
- `find-unused` on ProjectX Libraries identifies at least one known-dead helper method
- Zero regressions in existing commands after each addition

---

## Open Questions

- Should `extract-interface` also update the SimpleInjector registration automatically (like `add-dependency --register`)?
- Should `find-callers` and `find-unused` share a reference-counting core, or stay separate for clarity?
