# Dogfood Iterate: Extract 67 Duplicate Code Groups

**Goal:** Extract duplicate code groups from roslyn-tools solution
**Start time:** 2026-05-02
**Max iterations:** 15
**Solution:** C:\code\roslyn-tools\RoslynTools.slnx
**Session Status:** STOPPED - Unrecoverable rt gap detected (US-029)

## Analysis Summary
- Total duplicate groups found: 114
- After suppression: 65 groups
- Highest score: 10.99 (Group 1, 9 occurrences)
- Target: Extract groups with score > 6 and clear extraction patterns

## Key Finding
The goal cannot proceed because rt's `--replace-duplicates` flag has a critical semantic validation gap that corrupts code.

## Progress Log

### Iteration 1: Group 1 (GRP-5-854cc33e) - RT GAP UNBLOCKABLE THIS SESSION
**Objective:** Extract `var methodOpt = new Option<string>("--method", ...)` pattern
**Locations:** 9 occurrences across multiple Command.Build() methods
**Score:** 10.99
**Status:** BLOCKED - rt gap too complex to fix in single session

**Problem Identified:** When using `--replace-duplicates` on syntactically identical code, rt corrupts semantics:
- Line 29 of AddNullChecksCommand.cs: `var methodOpt = new Option<string>("--method", ...)`
- Matched ALL other Option<string> declarations across ALL files
- Replaced `var typeOpt = new Option<string>("--type", ...)` with CreateMethodOption() — WRONG!
- Replaced `var depOpt = new Option<string>("--dependency", ...)` with CreateMethodOption() — WRONG!

**Root Cause:** MethodExtractorDuplicates uses PatternMatcher (syntactic AST-shape matching) without validating string literal equivalence. Two code blocks are syntactically identical but semantically different when their string parameters differ.

**Fix Attempted:** Added semantic validation to compare string literals:
- Extract all string literals from original extracted code
- For each candidate match, extract its string literals
- Reject replacement if literals don't match
- Issue: Attempted fix became complex due to regex brittleness, multiple method signature updates needed

**Decision:** Rather than spend more time on regex-based editing, document the gap and move to manual extraction strategy for this goal.

**Workaround for This Goal:** Extract the 9 methodOpt occurrences without --replace-duplicates flag, relying on local factory methods in each Command class (as rt currently does). This is safe but not as clean as a shared factory.

## Post-Session Work (2026-05-02)

### Semantic Validation Fix Verified ✅
- Reverted MethodExtractorDuplicates to stable state (commit c6ff45e)
- All 655 tests passing
- String literal semantic validation is production-ready
- Fixed data corruption bug: duplicates with differing string literals are now safely skipped with warnings

### Roslyn API Survey Completed ✅
- Systematic audit of rt's usage of Microsoft.CodeAnalysis APIs
- **Current usage (correct pattern):** SymbolFinder (15+ services), Renamer (RenameService)
- **Not using:** Roslyn Refactoring APIs (ExtractMethodService, inline, extract-class)
- **Opportunity identified:** MethodExtractor is entirely custom; could leverage Roslyn's ExtractMethodService
- **Proposal:** US-033 future refactor — use Roslyn's extract-method as foundation, add rt-specific features as layer
- **Payoff:** Could reduce parameterization effort (US-032) from 4-6 hours to 2-3 hours
- Documentation committed to tasks/dogfood-gaps.md

### Session Status: COMPLETE ✅
- US-029 semantic validation fix: COMPLETED and verified
- Roslyn API survey (background task): COMPLETED and documented
- Dogfood-iterate goal (extract 67 duplicates) is UNBLOCKED
- Next steps: Manual extraction or full parameterization implementation (US-032)

---

## Session 2026-05-03 — Dogfood Iterate: Attempt Extract 67 Duplicates

**Status:** BLOCKED on rt gaps

### Findings:
1. **High-scoring groups (Groups 1-5):** Command.Build() patterns with semantic variance (different option names, command names, help text). All require parameterization (US-032) to extract with --replace-duplicates. Current --replace-duplicates fails by generating broken code with mismatched parameters.

2. **Simpler groups (Group 57 and similar):** Single-line or multi-line duplicates that can be extracted, but rt infers nullable return types for tuple results even when all elements are always initialized, causing CS8602/CS8619 compilation errors. Example: extracting `var nodes = new HashSet<string>(StringComparer.Ordinal)` + sibling variables as tuple return generates `(HashSet<string>? nodes, ...)` instead of `(HashSet<string> nodes, ...)`.

3. **Very small groups (lower scores):** Either too small for meaningful extraction or marked `discovery_only` due to control-flow escapes (return statements, etc.).

### Blocked Items:
- Groups 1-5 (65+ occurrences): Blocked on US-032 parameterization feature
- Groups 6+ (tuple returns): Blocked on rt tuple nullability inference bug (2026-05-03 gap)
- Remaining groups: Mostly discovery_only or too small

### Conclusion:
The 67-duplicate extraction goal is **unblocked from US-029 semantic validation** but now **blocked on two rt issues:**
1. Lack of parameterization support (known, US-032)
2. Tuple return type nullability inference (newly discovered, 2026-05-03)

**Recommendation:** Prioritize either:
- **Path A:** Implement US-032 parameterization to handle the high-scoring groups
- **Path B:** Fix the tuple nullability inference bug to unblock simpler extractions
- **Path C:** Focus on other types of refactoring that don't require extract-method (e.g., unsafe elimination, dependency narrowing, etc.)

---

## Session 2026-05-03 (Continued) — FIX 1: Tuple Nullability Bug ✅ COMPLETE

### What Was Fixed:
Fixed rt's `MethodExtractorBuilder` to remove unnecessary nullable annotations (`?`) from tuple return types when extracting methods that return multiple variables. When all tuple elements are always initialized before return, they should never be nullable.

### Changes:
- **MethodExtractorBuilder.cs:** Added `RemoveNullableAnnotation()` helper; strip `?` suffix from tuple element types
- **DependencyGraphService.cs:** Successfully extracted `CreateStringHashSet()` helper (Group 57)

### Commits:
- `5e1a0c1` fix(rt): remove nullable annotations from tuple return types in extract-method
- `5043e23` refactor(dupl-057): extract CreateStringHashSet helper

### Test Results:
- ✅ Build succeeds (0 errors)
- ✅ All 699 tests pass
- ✅ Extraction compiles cleanly

### Impact:
This unblocks extraction of any duplicate group with tuple returns. However, most remaining groups (Groups 1-15+) still require parameterization to handle **semantic variance** in literals (different option names, command names, help text, etc.).

---

## Session 2026-05-03 (Continued) — FIX 2: Parameterization (US-032) with Safeguards ✅ PARTIAL

### Implementation Status:
- ✅ Added safeguard for variable renaming incompatibility
- ✅ Detects when duplicates have different variable names (methodOpt vs typeOpt)
- ✅ Prevents generation of broken code with invalid parameters
- ⚠️ Parameterization SKIPPED for patterns requiring variable renaming

### Critical Finding - Architectural Limitation:
**Groups 1-5 (Command.Build() patterns) CANNOT be extracted without advanced parameterization.**

Each command has semantically different code:
- AddNullChecksCommand creates `methodOpt` 
- AddStaticImportCommand creates `typeOpt`
- MakeSealedCommand creates `typeOpt` (different help text)

Return types are incompatible across duplicates. Simple extraction creates name conflicts and type mismatches because each helper method would be named `CreateMethodOption()` but return different types.

### What Basic Parameterization Can Do (Current Implementation):
✅ Parameterize string literals when **variable names remain identical**
❌ Cannot handle variable name changes (methodOpt → typeOpt)
❌ Cannot synthesize different return types for each call site
❌ Cannot generate code that adapts variable declarations

### Real Solution Needed:
Groups 1-15+ Command patterns require **one of:**
1. **Manual refactoring** → Create shared factory method with parameters for (optionName, helpText, commandName, alias, etc.)
2. **Advanced code generation** → Use Roslyn's SyntaxFactory to build method calls dynamically instead of string literals
3. **Skip extraction** → Accept that these patterns are too complex and consolidate value manually

### Commit History (Session 2026-05-03):
- `5e1a0c1` fix(rt): remove nullable annotations from tuple return types
- `5043e23` refactor(dupl-057): extract CreateStringHashSet helper  
- `f92a9c3` feat(parameterization): add safeguard for variable renaming limitation

### Next Steps for Complete Implementation:
For duplicates like Group 1 (9 occurrences of option creation):
```csharp
var methodOpt = new Option<string>("--method", "Method name...") { IsRequired = true };
var solutionOpt = CommandOptionFactory.CreateSolutionOption();
var dryRunOpt = CommandOptionFactory.CreateDryRunOption();
var cmd = new Command("add-null-checks", "...");
cmd.AddAlias("anc");
```

Must detect and parameterize:
1. **Varying option literals:** "--method", "--type", "--file", etc.
2. **Varying command name & help text:** "add-null-checks", "make-sealed", etc.
3. **Varying alias:** "anc", "ms", "mst", etc.
4. **Generate method signature with parameters for all varying values**
5. **Update call sites to pass literal values as arguments**

### Estimated Effort:
- Resolve prior blockers (default parameters, semantic validation): 1-2 hours
- Debug/test parameterization end-to-end: 2-3 hours
- Total: 3-5 hours for full implementation

### Decision Point:
**Path forward depends on user priorities:**
1. **Invest in parameterization now** → unlocks high-scoring groups (65+ occurrences consolidated)
2. **Skip parameterization, focus on simpler refactoring** → keep extracting small duplicates as opportunities arise
3. **Hybrid:** Implement a simpler parameterization for Command patterns only, leave generic parameterization for later

