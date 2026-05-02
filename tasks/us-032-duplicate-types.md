# US-032 Duplicate Types Analysis

## Summary
- **Total groups found**: 114
- **Groups after suppression**: 65  
- **Currently extractable**: Limited (need to count)
- **Discovery-only (blocked)**: Majority

## Duplicate Types Currently NOT Extractable (Future Work)

### 1. Control Flow Escape: Return Statements
**Issue**: Block contains early return/break/continue statements  
**Impact**: Can't cleanly extract because the return/break/continue would be lost at call site

```csharp
// Example: Can't extract because method returns different values
var methodOpt = new Option<string>("--method", "...");
return someCondition ? methodOpt : null;  // ← return statement blocks extraction
```

**Workaround for future**: Could extract method returning a value, or use ref/out parameters

**Examples**:
- MakeInstanceCommand.Build vs MakeStaticCommand.Build (Group 57)
- ConvertToRecordCommand.Build vs MakeSealedCommand.Build (Group 58)
- ExtractInterfaceService.BuildAccessors vs InterfaceAnalyzerService.BuildAccessors (Group 59)
- JsonRenderer.ConvertValue vs YamlRenderer.ConvertValue (Group 60)

---

### 2. Multiple Output Variables  
**Issue**: Block produces 2+ outward variables; would need tuple return or out-params  
**Impact**: Can't extract without changing signature significantly

```csharp
// Example: Two outputs (methodSpec and newTypeName)
var methodSpec = BuildSpec(/* ... */);
var newTypeName = GetNewName(/* ... */);
// Can't extract both without tuple return
```

**Workaround for future**: 
- Support tuple returns
- Support out-parameters
- Create wrapper class/record

**Examples**:
- TransformParametersService.RunNewTypeAsync vs RunExistingTypeAsync (Group 62)

---

### 3. Identifier Mapping Complexity
**Issue**: Identifiers differ across duplicates in non-literal ways  
**Impact**: Can't parameterize identifier names (only literals currently)

```csharp
// Different identifier names, not just literals
var otpauthUri = AddUsingDirective(root, directive);  // One way
var globalUri = AddUsingDirective(root, directive);   // Different name
```

**Workaround for future**:
- Support identifier parameter substitution
- Detect and map identifier variations

**Examples**:
- ImportDirectiveService.AddStaticUsingAsync vs AddGlobalUsingAsync (Group 56 - actually extractable!)
- Dictionary variable names: JsonRenderer vs YamlRenderer (Group 63)

---

### 4. Special Return Statements (Not Simple Return)
**Issue**: Block has control flow that's hard to extract  
**Impact**: Pattern matching can't handle complex control flow

```csharp
// Early returns make extraction impossible
if (string.IsNullOrWhiteSpace(filePath))
    return false;  // ← Early return blocks extraction
```

**Workaround for future**: Extract the guard condition separately, then extract body

**Examples**:
- RoslynSyntax.FindDocumentId vs ConvertToRecordService.FindDocumentId (Group 61)
- ImportDirectiveService.IsGeneratedOrIntermediate vs MagicStringService.IsGeneratedOrIntermediate (Group 65)

---

## Current Solution: US-032 Parameterization

✅ **Now extractable**: Duplicates differing only in **string literal values**

Example:
```csharp
// Before
public void PrintA() { Console.WriteLine("hello"); Console.WriteLine("world"); }
public void PrintB() { Console.WriteLine("goodbye"); Console.WriteLine("universe"); }

// After parameterization
public void PrintMessages(string param0, string param1) 
{
    Console.WriteLine(param0); 
    Console.WriteLine(param1);
}
// Call sites: PrintMessages("hello", "world"); PrintMessages("goodbye", "universe");
```

---

## Next Project Candidates (Priority Order)

### 1. **Literal Identifier Parameters** (Medium Complexity)
- Similar to parameterization, but parameterize variable/field names  
- Impact: ~5-10% more duplicates
- Example: `var x = val` vs `var name = val`

### 2. **Control Flow Unwinding** (High Complexity)
- Extract methods with early returns as nested/guard clauses
- Impact: ~15-20% more duplicates  
- Example: Extract return statements as failures/guards

### 3. **Multiple Output Variables (Tuple Returns)** (Medium Complexity)
- Support C# tuple return types for methods with 2+ outputs
- Impact: ~5-10% more duplicates
- Example: `var (a, b) = Extract(); return (a, b);`

### 4. **Out/Ref Parameter Support** (Medium Complexity)
- Generate methods with `out` parameters for modified local variables
- Impact: ~5-10% more duplicates

### 5. **Type Parameter Variations** (High Complexity)
- Detect generic pattern variations and make extracted method generic
- Impact: ~10-15% more duplicates  
- Example: `List<T>` vs `Dictionary<T>`

---

## Metrics (roslyn-tools dogfood)

| Category | Count | Status |
|----------|-------|--------|
| Extractable (current) | ~1-2 | ✅ Implemented |
| Control flow escape | ~20-25 | 🔲 Future |
| Multiple outputs | ~2-3 | 🔲 Future |
| Identifier variations | ~10-15 | 🔲 Future |
| Type parameters | ~5-10 | 🔲 Future |
| Other | ~15-20 | 🔲 Future |
| **Total** | **65** | **Suppressed** |

---

## Architecture Notes

- **PatternMatcher**: Structurally identical code detection (agnostic to literal values)
- **MethodExtractorDuplicates**: Semantic validation + parameterization
- **Semantic Validation**: String literal set comparison (allows variations at specific positions)
- **Parameterization Pipeline**: 4 phases (detect → regenerate signature → rewrite body → update calls)
