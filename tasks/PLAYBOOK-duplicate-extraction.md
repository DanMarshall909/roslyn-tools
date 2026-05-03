# Playbook: Extract Duplicate Code Groups with rt

## Overview

This playbook documents how to safely extract duplicate code groups from the roslyn-tools solution using `rt refactor extract-method --replace-duplicates`, with semantic validation enabled (as of 2026-05-02).

## Prerequisites

- rt version with semantic validation fix (commit c6ff45e or later)
- Solution: `C:\code\roslyn-tools\RoslynTools.slnx`
- Know the file path and line range of the duplicate pattern to extract

## Workflow

### 1. Identify the Duplicate Group

Run to see all duplicate groups ranked by score:

```powershell
rt analyze find-duplicates --solution "C:\code\roslyn-tools\RoslynTools.slnx"
```

Pick the group with the highest score (lowest risk, most benefit). Note:
- **File path** of the pattern source
- **Line numbers** (start and end, 1-based, inclusive)
- **Preview** text (what the pattern looks like)
- **Score** (10.99 is highest priority)
- **Occurrences** (how many times it appears)

### 2. Choose an Extraction Name

Name the extracted method after what it does. Pattern: `Create{Thing}` or `Build{Component}`.

Examples:
- `CreateMethodOption` — for the `new Option<string>("--method", ...)` pattern
- `CreateSolutionOption` — for the shared solution option
- `BuildCommandHeader` — for the command Name + Alias pattern

### 3. Test with --dry-run First

Always preview before applying:

```powershell
rt refactor extract-method <file-path> <method-name> \
  --start-line <start> \
  --end-line <end> \
  --replace-duplicates \
  --solution "C:\code\roslyn-tools\RoslynTools.slnx" \
  --dry-run
```

### 4. Read the Dry-Run Output

**Key indicators:**

- **`matches found: N, replaced: M, skipped: K`**
  - `N` = total syntactically identical matches found
  - `M` = matches actually replaced (semantic equivalence confirmed)
  - `K` = matches skipped (string literals differ)
  - **Good**: `M > 0` and extraction makes sense
  - **Bad**: `M = 0` (no valid matches) or preview shows wrong code

- **`[WARNING] File:Line: string literal semantic mismatch — skipped`**
  - This is **expected and safe**
  - Means: syntactically identical code but with different string literals
  - Example: `new Option<string>("--method", ...)` vs `new Option<string>("--type", ...)`
  - These are **correctly skipped** to prevent data corruption

### 5. Validate the Diff

Read the `---` / `+++` diff carefully:

- ✅ **Good**: Extracted method called with correct parameters in each location
- ✅ **Good**: Warnings about skipped files due to semantic mismatch
- ❌ **Bad**: Wrong variables being replaced (e.g., `typeOpt` replaced with `CreateMethodOption()`)
- ❌ **Bad**: Method called with mismatched arguments (signature doesn't fit)

If the diff looks wrong, **do not apply**. Instead:
- Check the line range (off by one?)
- Check the extracted method name (conflicts with existing names?)
- File a dogfood gap in `tasks/dogfood-gaps.md`

### 6. Apply the Extraction

Once the dry-run is verified:

```powershell
rt refactor extract-method <file-path> <method-name> \
  --start-line <start> \
  --end-line <end> \
  --replace-duplicates \
  --solution "C:\code\roslyn-tools\RoslynTools.slnx"
```

(Same command, without `--dry-run`)

### 7. Build and Test

Verify the code still works:

```powershell
dotnet build "C:\code\roslyn-tools\RoslynTools.slnx" -c Release
dotnet test "C:\code\roslyn-tools\RoslynTools.slnx" -c Release -v minimal
```

Both must pass. If either fails, the extraction was wrong. Revert and re-diagnose.

### 8. Commit

```powershell
git add -A
git commit -m "refactor(dupl-NNN): extract <pattern-name>

Extracted <pattern> from <file>:<line-range>.
Replaced <count> semantically-equivalent occurrences across <file-count> files.
Skipped <skipped-count> semantically-different matches (string literal variance).

All tests passing.
Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>"
```

Replace:
- `NNN` — sequence number (001, 002, etc.)
- `<pattern-name>` — descriptive name (e.g., "MethodOption creation")
- `<count>` — how many replaced (from the dry-run output)
- `<file-count>` — how many files were touched
- `<skipped-count>` — how many were skipped due to semantic mismatch

## Common Scenarios

### Scenario A: Extraction Replaces 0 Matches

**Problem**: `replaced: 0` even though the pattern appears multiple times.

**Diagnosis**:
1. Are all occurrences truly identical (same strings, same structure)?
2. Is the line range correct (includes all statements in the pattern)?
3. Is the extracted method name unique (no naming conflicts)?

**Resolution**:
- Adjust the line range and retry
- Check if the pattern truly appears elsewhere or if find-duplicates had a false positive

### Scenario B: Some Matches Skipped with "string literal semantic mismatch"

**Problem**: Dry-run shows warnings about skipped files.

**Expected**: This is normal and safe! The semantic validation is working correctly.

**Action**: No action needed. The skipped matches were intentionally excluded because they have different string literals. Only the true semantic matches were replaced.

**Example**:
```
[WARNING] File:30: string literal semantic mismatch — skipped
[WARNING] File:31: string literal semantic mismatch — skipped
matches found: 9, replaced: 3, skipped: 6
```

This means: found 9 syntactically identical patterns, but 6 had different string literals (semantic variance). Only the 3 with identical literals were replaced. **This is correct.**

### Scenario C: Diff Shows Wrong Replacements

**Problem**: Diff shows `var typeOpt = ...` being replaced with `CreateMethodOption()`, but we extracted `var methodOpt = ...`.

**Diagnosis**: This should NOT happen with the semantic validation fix. If it does:
1. Double-check the line range (are we extracting the right code?)
2. File a bug report to `tasks/dogfood-gaps.md`

### Scenario D: Build Fails After Extraction

**Problem**: `dotnet build` fails with type errors after extraction.

**Diagnosis**:
- Parameter signature mismatch: the extracted method expects different parameters than what call sites have
- This can happen if the --replace-duplicates filter didn't catch a semantic difference

**Resolution**:
1. Revert the extraction: `git reset --hard HEAD~1`
2. Re-examine the dry-run output
3. If the issue is in rt, file a dogfood gap and skip this group for now

## State Tracking

After each extraction, update `tasks/dogfood-progress.md`:

```markdown
### Iteration N: Group X (GRP-Y-hash) - STATUS

**Pattern**: [description of what was extracted]
**Score**: [score from find-duplicates]
**Occurrences**: [count]
**Status**: ✅ COMPLETED / ⏸️ BLOCKED / ❌ FAILED
**Details**: 
- Replaced: [count] matches
- Skipped: [count] matches due to semantic mismatch
- Commit: [hash]
```

## Exit Criteria

**Stop when:**
1. All 65 duplicate groups (after suppression) are extracted, OR
2. An unrecoverable rt gap is encountered, OR
3. Max iteration limit is reached

## Notes

- **Semantic validation is enabled** (2026-05-02 fix): duplicates with different string literals are safely skipped with warnings
- **One extraction per commit**: easier to revert if needed
- **Always build + test**: don't batch extractions without verification
- **Safety first**: if dry-run looks wrong, don't apply it
