# Parameterization Playbook for --replace-duplicates

## Vision
Extend `--replace-duplicates` to parameterize duplicates that differ only in string literal values. Current coverage: 40-50 of 67 duplicate groups. Goal: consolidate 20 more groups (~30% additional coverage).

## Architecture
1. **Phase 1: Detect Variations** - Use ordered literal visitor to identify which positions vary across matches
2. **Phase 2: Regenerate Signature** - Add string parameters for varying positions
3. **Phase 3: Rewrite Body** - Replace varying-position literals with parameter refs
4. **Phase 4: Update Call Sites** - Pass literal values as arguments

## Implementation Strategy

### Phase 1: Literal Variation Detection
Location: `MethodExtractorDuplicates.cs`

```csharp
private static async Task<(List<List<string>>, HashSet<int>)> 
    AnalyzeLiteralVariationsAsync(Solution solution, IReadOnlyList<PatternMatch> matches)
```

**Purpose**: For each match, extract all string literals in traversal order. Identify positions where at least one match differs from the pattern.

**Key insight**: Use `OrderedLiteralVisitor` (extends CSharpSyntaxWalker) to visit literals in order, building `List<string>` for each match rather than `HashSet<string>`.

**Input**: All matches from `PatternMatcher.FindMatchesAsync`
**Output**: 
- `List<List<string>>` - literals by match index, in order
- `HashSet<int>` - positions where values vary

### Phase 2: Signature Regeneration
Location: `MethodExtractorDuplicates.cs`, called from `ApplyMatchesAsync`

```csharp
private static async Task<Solution> RegenerateForParameterizedLiteralsAsync(
    Solution solution,
    string filePath,
    string newMethodName,
    List<List<string>> literalsByMatch,
    HashSet<int> varyingPositions)
```

**Purpose**: If varyingPositions.Count > 0, find the newly-extracted method and add parameters.

**Steps**:
1. Locate the extracted method in `filePath` by name
2. Build parameter list: `string param0, string param1, ...` for each varying position
3. Call `RegenerateMethodSignature` to add them to the method declaration
4. Call `RewriteMethodBodyForParameters` to update the body
5. Replace in solution and return

**Key detail**: Parameter names follow pattern `paramN` where N is the position index. Future: infer contextual names (e.g., "message" from usage context).

### Phase 3: Body Rewriting
Location: `MethodExtractorDuplicates.cs`

```csharp
private static MethodDeclarationSyntax RewriteMethodBodyForParameters(
    MethodDeclarationSyntax methodDecl,
    List<string> patternLiterals,
    HashSet<int> varyingPositions)
```

**Purpose**: Replace all literal values at varying positions with parameter references.

**Implementation**:
- Create `LiteralReplacementRewriter : CSharpSyntaxWalker`
- Track literal index as we traverse
- When index matches a varying position, replace `LiteralExpressionSyntax` with `IdentifierName(paramN)`
- Preserves non-varying literals (hardcoded)

### Phase 4: Call Site Rewriting
Location: `MethodExtractorDuplicates.cs`, in `TryReplaceMatchAsync`

**Purpose**: For each match, extract its literal values and pass them as arguments.

**Key detail**: Arguments list = [existing params] + [literal values in position order]

**Example**:
```
Pattern: Console.WriteLine("hello"); Console.WriteLine("world");
Extracted: PrintMessages(string msg1, string msg2)

Match A: Console.WriteLine("hello"); Console.WriteLine("world");
  -> PrintMessages("hello", "world");

Match B: Console.WriteLine("goodbye"); Console.WriteLine("universe");  
  -> PrintMessages("goodbye", "universe");
```

## Integration Points

### ApplyMatchesAsync (Main Flow)
1. Call `AnalyzeLiteralVariationsAsync` at the top to get varying positions
2. If varyingPositions.Count > 0 and matches.Count > 1:
   - Call `RegenerateForParameterizedLiteralsAsync` before applying matches
   - Track that method was regenerated (to avoid duplicate parameterization)
3. Classify matches:
   - Non-parameterized: identical literals → original flow
   - Parameterized: varying literals → pass to `TryReplaceMatchAsync` with literal values

### TryReplaceMatchAsync (Call Site Writing)
1. Receive `varyingPositions` and `matchLiterals` as new parameters
2. For each varying position, append the literal value to args list
3. Build call site with all arguments

## Testing Strategy

### Test Case: Two Files, Varying Strings
```csharp
// A.cs
System.Console.WriteLine("hello");
System.Console.WriteLine("world");

// B.cs  
System.Console.WriteLine("goodbye");
System.Console.WriteLine("universe");
```

Expected result after extraction:
- A: `PrintMessages("hello", "world");`
- B: `PrintMessages("goodbye", "universe");`
- Method signature: `private void PrintMessages(string param0, string param1)`

### Regression Tests
- Non-parameterized duplicates still work (identical literals)
- Single occurrence extracts without error
- Overlapping matches still detected and skipped
- Dry-run shows changes without writing

## Known Issues & Workarounds

1. **Literal Indexing**: Ordered visitor must match literal positions across all matches perfectly.
   - Workaround: If position count mismatches, skip parameterization for that group

2. **Parameter Naming**: `paramN` is generic. Contextual naming (e.g., "message", "helpText") is deferred.
   - Current: always use `paramN`
   - Future: analyze usage context to infer better names

3. **Literal Types**: Currently only handles string literals.
   - Numeric, bool, null literals could also be parameterized
   - Deferred for Phase 2 (current scope: strings only)

## Future Enhancements

- [ ] Infer parameter names from context (AST analysis)
- [ ] Support numeric/bool/null literal parameterization
- [ ] Test with 20+ duplicates from real codebase
- [ ] Performance profiling for large match lists
- [ ] Error recovery: if parameterization fails, fall back to non-parameterized replacement
