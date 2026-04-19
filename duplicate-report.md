# Duplicate Groups

## Group 1: GRP-8-35922061

**Extractability:** extractable  
**Score:** 14.33  
**Occurrences:** 6  

**Preview:**

```csharp
var typeOpt = new Option<string>("--type", "Class to convert to a record")
{
    IsRequired = true
};
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("convert-to-record", "Convert a class to a record type with positional parameters");
cmd.AddAlias("ctr");
cmd.Add(typeOpt);
cmd.Add(solutionOpt);
cmd.Add(dryRunOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ConvertToRecordCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ConvertToRecordCommand.cs#L27">ConvertToRecordCommand.cs:27-35</a> | ConvertToRecordCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindCallersCommand.cs:32" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindCallersCommand.cs#L32">FindCallersCommand.cs:32-40</a> | FindCallersCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FixNamespaceCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FixNamespaceCommand.cs#L30">FixNamespaceCommand.cs:30-38</a> | FixNamespaceCommand | Build |
| 4 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeInstanceCommand.cs:26" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeInstanceCommand.cs#L26">MakeInstanceCommand.cs:26-34</a> | MakeInstanceCommand | Build |
| 5 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeSealedCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeSealedCommand.cs#L27">MakeSealedCommand.cs:27-35</a> | MakeSealedCommand | Build |
| 6 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeStaticCommand.cs:29" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeStaticCommand.cs#L29">MakeStaticCommand.cs:29-37</a> | MakeStaticCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 2: GRP-6-e057751c

**Extractability:** extractable  
**Score:** 9.66  
**Occurrences:** 5  

**Preview:**

```csharp
var cmd = new Command("add-null-checks", "Insert ArgumentNullException.ThrowIfNull guards for reference-type parameters");
cmd.AddAlias("anc");
cmd.Add(typeOpt);
cmd.Add(methodOpt);
cmd.Add(solutionOpt);
cmd.Add(dryRunOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs:35" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs#L35">AddNullChecksCommand.cs:35-40</a> | AddNullChecksCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs:41" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs#L41">FindDuplicatesCommand.cs:41-46</a> | FindDuplicatesCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ImplementInterfaceCommand.cs:32" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ImplementInterfaceCommand.cs#L32">ImplementInterfaceCommand.cs:32-37</a> | ImplementInterfaceCommand | Build |
| 4 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs:41" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs#L41">InlineMethodCommand.cs:41-43</a> | InlineMethodCommand | Build |
| 5 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/SafeDeleteCommand.cs:36" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/SafeDeleteCommand.cs#L36">SafeDeleteCommand.cs:36-41</a> | SafeDeleteCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 3: GRP-10-56673040

**Extractability:** extractable  
**Score:** 6.93  
**Occurrences:** 2  

**Preview:**

```csharp
var typeOpt = new Option<string>("--type", "Class to add interface stubs to")
{
    IsRequired = true
};
var interfaceOpt = new Option<string?>("--interface", "Interface to implement (optional; auto-detected if omitted)");
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("implement-interface", "Generate throw-stub implementations for unimplemented interface members");
cmd.AddAlias("ii");
cmd.Add(typeOpt);
cmd.Add(interfaceOpt);
cmd.Add(solutionOpt);
cmd.Add(dryRunOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ImplementInterfaceCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ImplementInterfaceCommand.cs#L27">ImplementInterfaceCommand.cs:27-37</a> | ImplementInterfaceCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/SafeDeleteCommand.cs:31" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/SafeDeleteCommand.cs#L31">SafeDeleteCommand.cs:31-41</a> | SafeDeleteCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 4: GRP-3-2cbe8bd6

**Extractability:** extractable  
**Score:** 6.59  
**Occurrences:** 9  

**Preview:**

```csharp
var nameOpt = new Option<string?>("--name", "Method name for single-test mode");
var acsOpt = new Option<string?>("--acs", "Semicolon-separated acceptance criteria (AC mode)");
var nestedClassOpt = new Option<string?>("--nested-class", "Wrap AC-generated methods in a nested class with this name");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs:28" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs#L28">AddTestCommand.cs:28-30</a> | AddTestCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs:29" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs#L29">AddTestCommand.cs:29-31</a> | AddTestCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs#L30">AddTestCommand.cs:30-32</a> | AddTestCommand | Build |
| 4 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/DependencyGraphCommand.cs:34" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/DependencyGraphCommand.cs#L34">DependencyGraphCommand.cs:34-36</a> | DependencyGraphCommand | Build |
| 5 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs:33" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs#L33">ExtractInterfaceCommand.cs:33-35</a> | ExtractInterfaceCommand | Build |
| 6 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractMethodCommand.cs:33" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractMethodCommand.cs#L33">ExtractMethodCommand.cs:33-35</a> | ExtractMethodCommand | Build |
| 7 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MoveCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MoveCommand.cs#L27">MoveCommand.cs:27-29</a> | MoveCommand | Build |
| 8 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/RenameCommand.cs:39" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/RenameCommand.cs#L39">RenameCommand.cs:39-41</a> | RenameCommand | Build |
| 9 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/TransformParametersCommand.cs:28" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/TransformParametersCommand.cs#L28">TransformParametersCommand.cs:28-30</a> | TransformParametersCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 5: GRP-6-321a7fd6

**Extractability:** extractable  
**Score:** 6.59  
**Occurrences:** 3  

**Preview:**

```csharp
var depOpt = new Option<string>("--dependency", "Dependency type to inject (e.g. IEmailService)")
{
    IsRequired = true
};
var fieldOpt = new Option<string?>("--field", "Field name override (default: derived from type)");
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("add-dependency", "Inject a constructor dependency into a class");
cmd.AddAlias("ad");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs:31" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs#L31">AddDependencyCommand.cs:31-37</a> | AddDependencyCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ImplementInterfaceCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ImplementInterfaceCommand.cs#L27">ImplementInterfaceCommand.cs:27-33</a> | ImplementInterfaceCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/SafeDeleteCommand.cs:31" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/SafeDeleteCommand.cs#L31">SafeDeleteCommand.cs:31-37</a> | SafeDeleteCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 6: GRP-4-5e3ef0f3

**Extractability:** extractable  
**Score:** 6.44  
**Occurrences:** 5  

**Preview:**

```csharp
var nestedClassOpt = new Option<string?>("--nested-class", "Wrap AC-generated methods in a nested class with this name");
var fileOpt = new Option<string?>("--file", "Path to the .cs file containing the test class");
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs#L30">AddTestCommand.cs:30-33</a> | AddTestCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs:33" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs#L33">ExtractInterfaceCommand.cs:33-36</a> | ExtractInterfaceCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractMethodCommand.cs:33" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractMethodCommand.cs#L33">ExtractMethodCommand.cs:33-36</a> | ExtractMethodCommand | Build |
| 4 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/RenameCommand.cs:39" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/RenameCommand.cs#L39">RenameCommand.cs:39-42</a> | RenameCommand | Build |
| 5 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/TransformParametersCommand.cs:28" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/TransformParametersCommand.cs#L28">TransformParametersCommand.cs:28-31</a> | TransformParametersCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 7: GRP-5-9f947763

**Extractability:** extractable  
**Score:** 5.49  
**Occurrences:** 3  

**Preview:**

```csharp
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("add-dependency", "Inject a constructor dependency into a class");
cmd.AddAlias("ad");
cmd.AddAll(typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs:33" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs#L33">AddDependencyCommand.cs:33-38</a> | AddDependencyCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs:35" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs#L35">ExtractInterfaceCommand.cs:35-40</a> | ExtractInterfaceCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeAsyncCommand.cs:35" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeAsyncCommand.cs#L35">MakeAsyncCommand.cs:35-40</a> | MakeAsyncCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 8: GRP-4-9ad37544

**Extractability:** extractable  
**Score:** 4.39  
**Occurrences:** 3  

**Preview:**

```csharp
var typeOpt = new Option<string>("--type", "Class containing the method")
{
    IsRequired = true
};
var methodOpt = new Option<string>("--method", "Method name to add null checks to")
{
    IsRequired = true
};
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs#L30">AddNullChecksCommand.cs:30-33</a> | AddNullChecksCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs:36" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs#L36">InlineMethodCommand.cs:36-39</a> | InlineMethodCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ReplaceCallCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ReplaceCallCommand.cs#L27">ReplaceCallCommand.cs:27-30</a> | ReplaceCallCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 9: GRP-4-2596a6fe

**Extractability:** extractable  
**Score:** 4.39  
**Occurrences:** 3  

**Preview:**

```csharp
var classOpt = new Option<string>("--class", "Test class name to add methods to")
{
    IsRequired = true
};
var nameOpt = new Option<string?>("--name", "Method name for single-test mode");
var acsOpt = new Option<string?>("--acs", "Semicolon-separated acceptance criteria (AC mode)");
var nestedClassOpt = new Option<string?>("--nested-class", "Wrap AC-generated methods in a nested class with this name");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs#L27">AddTestCommand.cs:27-30</a> | AddTestCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs:32" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs#L32">ExtractInterfaceCommand.cs:32-35</a> | ExtractInterfaceCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/TransformParametersCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/TransformParametersCommand.cs#L27">TransformParametersCommand.cs:27-30</a> | TransformParametersCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 10: GRP-4-575827a9

**Extractability:** extractable  
**Score:** 4.39  
**Occurrences:** 3  

**Preview:**

```csharp
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var cmd = new Command("dependency-graph", "Build a type-level dependency graph for the solution");
cmd.AddAlias("depgraph");
cmd.AddAll(typeOpt, depthOpt, formatOpt, scopeOpt, solutionOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/DependencyGraphCommand.cs:36" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/DependencyGraphCommand.cs#L36">DependencyGraphCommand.cs:36-40</a> | DependencyGraphCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindPatternCommand.cs:31" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindPatternCommand.cs#L31">FindPatternCommand.cs:31-35</a> | FindPatternCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ListSmellsCommand.cs:34" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ListSmellsCommand.cs#L34">ListSmellsCommand.cs:34-38</a> | ListSmellsCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 11: GRP-3-acd1afec

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 4  

**Preview:**

```csharp
var typeOpt = new Option<string>("--type", "Target class name")
{
    IsRequired = true
};
var depOpt = new Option<string>("--dependency", "Dependency type to inject (e.g. IEmailService)")
{
    IsRequired = true
};
var fieldOpt = new Option<string?>("--field", "Field name override (default: derived from type)");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs#L30">AddDependencyCommand.cs:30-32</a> | AddDependencyCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs#L30">AddNullChecksCommand.cs:30-32</a> | AddNullChecksCommand | Build |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs:36" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs#L36">InlineMethodCommand.cs:36-38</a> | InlineMethodCommand | Build |
| 4 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ReplaceCallCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ReplaceCallCommand.cs#L27">ReplaceCallCommand.cs:27-29</a> | ReplaceCallCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 12: GRP-6-a6a23680

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 2  

**Preview:**

```csharp
var fieldOpt = new Option<string?>("--field", "Field name override (default: derived from type)");
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("add-dependency", "Inject a constructor dependency into a class");
cmd.AddAlias("ad");
cmd.AddAll(typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs:32" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs#L32">AddDependencyCommand.cs:32-38</a> | AddDependencyCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs:34" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs#L34">ExtractInterfaceCommand.cs:34-40</a> | ExtractInterfaceCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 13: GRP-6-777806d9

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 2  

**Preview:**

```csharp
var fileOpt = new Option<string?>("--file", "Path to the .cs file containing the test class");
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("add-test", "Append xUnit test stubs to an existing test class");
cmd.AddAlias("at");
cmd.AddAll(classOpt, nameOpt, acsOpt, nestedClassOpt, fileOpt, solutionOpt, dryRunOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs:31" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs#L31">AddTestCommand.cs:31-37</a> | AddTestCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractClassCommand.cs:36" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractClassCommand.cs#L36">ExtractClassCommand.cs:36-42</a> | ExtractClassCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 14: GRP-6-7541cf09

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 2  

**Preview:**

```csharp
var nestedClassOpt = new Option<string?>("--nested-class", "Wrap AC-generated methods in a nested class with this name");
var fileOpt = new Option<string?>("--file", "Path to the .cs file containing the test class");
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("add-test", "Append xUnit test stubs to an existing test class");
cmd.AddAlias("at");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs#L30">AddTestCommand.cs:30-36</a> | AddTestCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs:33" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs#L33">ExtractInterfaceCommand.cs:33-39</a> | ExtractInterfaceCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 15: GRP-6-59762d01

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 2  

**Preview:**

```csharp
var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var cmd = new Command("circular-deps", "Detect circular namespace/type dependencies in the solution");
cmd.AddAlias("cdeps");
cmd.Add(scopeOpt);
cmd.Add(solutionOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/CircularDepsCommand.cs:32" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/CircularDepsCommand.cs#L32">CircularDepsCommand.cs:32-38</a> | CircularDepsCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindUnusedCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindUnusedCommand.cs#L30">FindUnusedCommand.cs:30-36</a> | FindUnusedCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 16: GRP-5-47a54c10

**Extractability:** extractable  
**Score:** 3.47  
**Occurrences:** 2  

**Preview:**

```csharp
var typeOpt = new Option<string>("--type", "Class to extract interface from")
{
    IsRequired = true
};
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var outputDirOpt = new Option<string?>("--output-dir", "Directory for the new interface file");
var namespaceOpt = new Option<string?>("--namespace", "Namespace override for the interface");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs:32" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractInterfaceCommand.cs#L32">ExtractInterfaceCommand.cs:32-36</a> | ExtractInterfaceCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/TransformParametersCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/TransformParametersCommand.cs#L27">TransformParametersCommand.cs:27-31</a> | TransformParametersCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 17: GRP-5-afbf9a64

**Extractability:** extractable  
**Score:** 3.47  
**Occurrences:** 2  

**Preview:**

```csharp
var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var cmd = new Command("dependency-graph", "Build a type-level dependency graph for the solution");
cmd.AddAlias("depgraph");
cmd.AddAll(typeOpt, depthOpt, formatOpt, scopeOpt, solutionOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/DependencyGraphCommand.cs:35" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/DependencyGraphCommand.cs#L35">DependencyGraphCommand.cs:35-40</a> | DependencyGraphCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindPatternCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindPatternCommand.cs#L30">FindPatternCommand.cs:30-35</a> | FindPatternCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 18: GRP-5-232f4bde

**Extractability:** extractable  
**Score:** 3.47  
**Occurrences:** 2  

**Preview:**

```csharp
cmd.Add(solutionOpt);
cmd.Add(minStatementsOpt);
cmd.Add(minOccurrencesOpt);
cmd.Add(scopeOpt);
cmd.Add(includeTestsOpt);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs:43" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs#L43">FindDuplicatesCommand.cs:43-47</a> | FindDuplicatesCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs:44" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs#L44">FindDuplicatesCommand.cs:44-48</a> | FindDuplicatesCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 19: GRP-4-0d40b3f9

**Extractability:** extractable  
**Score:** 2.77  
**Occurrences:** 2  

**Preview:**

```csharp
var membersOpt = new Option<string>("--members", "Comma-separated member names to extract")
{
    IsRequired = true
};
var newTypeOpt = new Option<string>("--new-type", "Name for the new class")
{
    IsRequired = true
};
var staticOpt = new Option<bool>("--static", "Make the new class static (no injection)");
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractClassCommand.cs:33" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractClassCommand.cs#L33">ExtractClassCommand.cs:33-36</a> | ExtractClassCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeAsyncCommand.cs:32" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeAsyncCommand.cs#L32">MakeAsyncCommand.cs:32-35</a> | MakeAsyncCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 20: GRP-4-a1d63c18

**Extractability:** extractable  
**Score:** 2.77  
**Occurrences:** 2  

**Preview:**

```csharp
var nameOpt = new Option<string?>("--name", "Method name for single-test mode");
var acsOpt = new Option<string?>("--acs", "Semicolon-separated acceptance criteria (AC mode)");
var nestedClassOpt = new Option<string?>("--nested-class", "Wrap AC-generated methods in a nested class with this name");
var fileOpt = new Option<string?>("--file", "Path to the .cs file containing the test class");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs:28" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs#L28">AddTestCommand.cs:28-31</a> | AddTestCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs:29" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddTestCommand.cs#L29">AddTestCommand.cs:29-32</a> | AddTestCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 21: GRP-4-5ac59011

**Extractability:** extractable  
**Score:** 2.77  
**Occurrences:** 2  

**Preview:**

```csharp
var startLineOpt = new Option<int>("--start-line", "First line of the statements to extract (1-based).")
{
    IsRequired = true
};
var endLineOpt = new Option<int>("--end-line", "Last line of the statements to extract (1-based, inclusive).")
{
    IsRequired = true
};
var afterOpt = new Option<string?>("--after", "Insert the new method after this member name. Default: after the containing method.");
var beforeOpt = new Option<string?>("--before", "Insert the new method before this member name.");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractMethodCommand.cs:31" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractMethodCommand.cs#L31">ExtractMethodCommand.cs:31-34</a> | ExtractMethodCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindPatternCommand.cs:28" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindPatternCommand.cs#L28">FindPatternCommand.cs:28-31</a> | FindPatternCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 22: GRP-12-46c7d4fa

**Extractability:** discovery_only  
**Score:** 2.50  
**Occurrences:** 2  

**Preview:**

```csharp
var typeOpt = new Option<string>("--type", "Class containing the method")
{
    IsRequired = true
};
var methodOpt = new Option<string>("--method", "Method name to add null checks to")
{
    IsRequired = true
};
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("add-null-checks", "Insert ArgumentNullException.ThrowIfNull guards for reference-type parameters");
cmd.AddAlias("anc");
cmd.Add(typeOpt);
cmd.Add(methodOpt);
cmd.Add(solutionOpt);
cmd.Add(dryRunOpt);
cmd.SetHandler(async (type, method, solution, dryRun) =>
{
    await ExecuteAsync(type, method, solution, dryRun);
}, typeOpt, methodOpt, solutionOpt, dryRunOpt);
return cmd;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs#L30">AddNullChecksCommand.cs:30-47</a> | AddNullChecksCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs:36" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs#L36">InlineMethodCommand.cs:36-50</a> | InlineMethodCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 23: GRP-11-5a5ffaa7

**Extractability:** discovery_only  
**Score:** 2.29  
**Occurrences:** 2  

**Preview:**

```csharp
if (node.Identifier.Text != _targetClass)
    return base.VisitClassDeclaration(node);
var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
// Skip if the field already exists
if (visited.Members.OfType<FieldDeclarationSyntax>().Any(f => f.Declaration.Variables.Any(v => v.Identifier.Text == _fieldName)))
    return visited;
// Add the field after existing fields
var fieldDecl = SyntaxFactory.ParseMemberDeclaration($"    private readonly {_depType} {_fieldName};\n")!;
var members = visited.Members;
int insertIndex = 0;
for (int i = 0; i < members.Count; i++)
{
    if (members[i] is FieldDeclarationSyntax)
        insertIndex = i + 1;
    else
        break;
}
members = members.Insert(insertIndex, fieldDecl);
visited = visited.WithMembers(members);
var constructors = visited.Members.OfType<ConstructorDeclarationSyntax>().ToList();
if (constructors.Count == 0)
{
    // Generate a new constructor
    var ctorText = $"    public {visited.Identifier.Text}({_depType} {_paramName})\n" + $"    {{\n" + $"        {_fieldName} = {_paramName};\n" + $"    }}\n";
    var ctorDecl = SyntaxFactory.ParseMemberDeclaration(ctorText)!;
    members = visited.Members.Insert(insertIndex + 1, ctorDecl);
    visited = visited.WithMembers(members);
}
else
{
    // Add parameter + assignment to existing constructors
    var replacements = new List<(ConstructorDeclarationSyntax Orig, ConstructorDeclarationSyntax Repl)>();
    foreach (var ctor in constructors)
    {
        if (ctor.ParameterList.Parameters.Any(p => p.Type?.ToString().Contains(_depType) == true))
            continue;
        var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(_paramName)).WithType(SyntaxFactory.ParseTypeName(_depType + " "));
        var newCtor = ctor.WithParameterList(ctor.ParameterList.AddParameters(newParam));
        var assignment = SyntaxFactory.ParseStatement($"        {_fieldName} = {_paramName};\n");
        if (newCtor.Body != null)
            newCtor = newCtor.WithBody(newCtor.Body.WithStatements(newCtor.Body.Statements.Insert(0, assignment)));
        replacements.Add((ctor, newCtor));
    }

    if (replacements.Count > 0)
        visited = visited.ReplaceNodes(replacements.Select(r => r.Orig), (orig, _) => replacements.First(r => ReferenceEquals(r.Orig, orig)).Repl);
}
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs:140" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddDependencyCommand.cs#L140">AddDependencyCommand.cs:140-202</a> | DependencyInjector | VisitClassDeclaration |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractClassCommand.cs:246" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ExtractClassCommand.cs#L246">ExtractClassCommand.cs:246-306</a> | DependencyInjector | VisitClassDeclaration |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | warn | captured fields/properties differ across occurrences; review before extraction |

## Group 24: GRP-3-3de5b561

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var minStatementsOpt = new Option<int>("--min-statements", () => 3, "Minimum number of statements in a duplicate window");
var minOccurrencesOpt = new Option<int>("--min-occurrences", () => 2, "Minimum number of occurrences to report a group");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs:34" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs#L34">FindDuplicatesCommand.cs:34-36</a> | FindDuplicatesCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ListSmellsCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ListSmellsCommand.cs#L30">ListSmellsCommand.cs:30-32</a> | ListSmellsCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 25: GRP-3-461b0210

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var declarationFiles = new List<string>();
string? projectDir = null;
string? rootNamespace = null;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/MoveCore.cs:79" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/MoveCore.cs#L79">MoveCore.cs:79-81</a> | MoveCore | ScanSolutionForNamespaceAsync |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/MoveCore.cs:165" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/MoveCore.cs#L165">MoveCore.cs:165-167</a> | MoveCore | CollectDeclarationPaths |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 26: GRP-3-25577baf

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var nodes = new HashSet<string>(StringComparer.Ordinal);
var edgeSet = new HashSet<(string, string)>();
var edges = new List<(string From, string To)>();
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/DependencyGraphCore.cs:92" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/DependencyGraphCore.cs#L92">DependencyGraphCore.cs:92-94</a> | DependencyGraphCore | BuildFullGraph |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/DependencyGraphCore.cs:124" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/DependencyGraphCore.cs#L124">DependencyGraphCore.cs:124-126</a> | DependencyGraphCore | BuildBfsGraph |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 27: GRP-3-14e87dc5

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var symbolArg = new Argument<string>("symbol", "Fully-qualified interface or abstract class name");
var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
var cmd = new Command("find-impl", "Find all implementations of an interface or abstract class (JSON)")
{
    symbolArg,
    solutionOpt
};
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindImplementationsCommand.cs:30" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindImplementationsCommand.cs#L30">FindImplementationsCommand.cs:30-36</a> | FindImplementationsCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/OutlineCommand.cs:29" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/OutlineCommand.cs#L29">OutlineCommand.cs:29-35</a> | OutlineCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 28: GRP-3-5636e9d1

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var minStatementsOpt = new Option<int>("--min-statements", () => 3, "Minimum number of statements in a duplicate window");
var minOccurrencesOpt = new Option<int>("--min-occurrences", () => 2, "Minimum number of occurrences to report a group");
var scopeOpt = new Option<string?>("--scope", "Project name or namespace prefix to restrict search to");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs:35" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/FindDuplicatesCommand.cs#L35">FindDuplicatesCommand.cs:35-37</a> | FindDuplicatesCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ListSmellsCommand.cs:32" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ListSmellsCommand.cs#L32">ListSmellsCommand.cs:32-34</a> | ListSmellsCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 29: GRP-3-fc0b1281

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var document = sln.GetDocument(docId)!;
var root = (await document.GetSyntaxRootAsync())!;
var oldNode = root.FindNode(syntaxRef.Span) as ClassDeclarationSyntax ?? throw new InvalidOperationException("Cannot locate class syntax node in document.");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/ConvertToRecordCore.cs:214" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/ConvertToRecordCore.cs#L214">ConvertToRecordCore.cs:214-217</a> | ConvertToRecordCore | ApplyRecordReplacementAsync |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/MakeSealedCore.cs:72" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/MakeSealedCore.cs#L72">MakeSealedCore.cs:72-75</a> | MakeSealedCore | AddSealedModifierAsync |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 30: GRP-10-90521036

**Extractability:** discovery_only  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var typeOpt = new Option<string>("--type", "Class to convert to a record")
{
    IsRequired = true
};
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("convert-to-record", "Convert a class to a record type with positional parameters");
cmd.AddAlias("ctr");
cmd.Add(typeOpt);
cmd.Add(solutionOpt);
cmd.Add(dryRunOpt);
cmd.SetHandler(async (type, solution, dryRun) =>
{
    await ExecuteAsync(type, solution, dryRun);
}, typeOpt, solutionOpt, dryRunOpt);
return cmd;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/ConvertToRecordCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/ConvertToRecordCommand.cs#L27">ConvertToRecordCommand.cs:27-42</a> | ConvertToRecordCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeSealedCommand.cs:27" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeSealedCommand.cs#L27">MakeSealedCommand.cs:27-42</a> | MakeSealedCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 31: GRP-10-2028b83d

**Extractability:** discovery_only  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var methodOpt = new Option<string>("--method", "Fully-qualified method to convert: Type.Method")
{
    IsRequired = true
};
var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
var cmd = new Command("make-instance", "Remove static from a method and rewrite call sites to use an instance");
cmd.AddAlias("mns");
cmd.Add(methodOpt);
cmd.Add(solutionOpt);
cmd.Add(dryRunOpt);
cmd.SetHandler(async (method, solution, dryRun) =>
{
    await ExecuteAsync(method, solution, dryRun);
}, methodOpt, solutionOpt, dryRunOpt);
return cmd;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeInstanceCommand.cs:26" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeInstanceCommand.cs#L26">MakeInstanceCommand.cs:26-41</a> | MakeInstanceCommand | Build |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeStaticCommand.cs:29" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeStaticCommand.cs#L29">MakeStaticCommand.cs:29-44</a> | MakeStaticCommand | Build |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 32: GRP-4-9ffd7dad

**Extractability:** discovery_only  
**Score:** 1.66  
**Occurrences:** 4  

**Preview:**

```csharp
var location = method.Locations.FirstOrDefault(l => l.IsInSource);
if (location == null)
    return null;
foreach (var project in sln.Projects)
{
    foreach (var doc in project.Documents)
    {
        if (doc.FilePath != null && string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
            return doc.Id;
    }
}
return null;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs:165" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs#L165">AddNullChecksCommand.cs:165-177</a> | AddNullChecksCommand | FindDocumentIdAsync |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs:272" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/InlineMethodCommand.cs#L272">InlineMethodCommand.cs:272-284</a> | InlineMethodCommand | FindDocumentIdContainingMethod |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeAsyncCommand.cs:280" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeAsyncCommand.cs#L280">MakeAsyncCommand.cs:280-292</a> | MakeAsyncCommand | FindDocumentIdContainingMethod |
| 4 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/MakeStaticCore.cs:76" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/MakeStaticCore.cs#L76">MakeStaticCore.cs:76-90</a> | MakeStaticCore | FindDocumentIdForSymbolAsync |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 33: GRP-4-e14fe062

**Extractability:** discovery_only  
**Score:** 1.66  
**Occurrences:** 4  

**Preview:**

```csharp
var existing = root.Usings.Select(u => u.Name?.ToString().Trim()).ToHashSet(StringComparer.Ordinal);
if (existing.Contains(ns))
    return root;
var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(" " + ns)).WithTrailingTrivia(SyntaxFactory.LineFeed);
return root.AddUsings(newUsing);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs:182" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/AddNullChecksCommand.cs#L182">AddNullChecksCommand.cs:182-193</a> | AddNullChecksCommand | EnsureUsing |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Commands/MakeAsyncCommand.cs:297" title="file:///C:/code/roslyn-tools/src/RoslynTools/Commands/MakeAsyncCommand.cs#L297">MakeAsyncCommand.cs:297-308</a> | MakeAsyncCommand | EnsureUsing |
| 3 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/AddTestCore.cs:149" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/AddTestCore.cs#L149">AddTestCore.cs:149-160</a> | AddTestCore | EnsureUsing |
| 4 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/ImplementInterfaceCore.cs:288" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/ImplementInterfaceCore.cs#L288">ImplementInterfaceCore.cs:288-299</a> | ImplementInterfaceCore | EnsureUsing |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 34: GRP-4-0c55943c

**Extractability:** discovery_only  
**Score:** 0.83  
**Occurrences:** 2  

**Preview:**

```csharp
var location = classSymbol.Locations.FirstOrDefault(l => l.IsInSource);
if (location == null)
    return null;
foreach (var project in sln.Projects)
{
    foreach (var doc in project.Documents)
    {
        if (doc.FilePath != null && string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return doc.Id;
        }
    }
}
return null;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/ConvertToRecordCore.cs:63" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/ConvertToRecordCore.cs#L63">ConvertToRecordCore.cs:63-78</a> | ConvertToRecordCore | FindDocumentId |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/MakeSealedCore.cs:106" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/MakeSealedCore.cs#L106">MakeSealedCore.cs:106-121</a> | MakeSealedCore | FindDocumentIdAsync |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 35: GRP-3-5975bfd1

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
var originalSln = sln;
var(methodSymbol, doc) = await FindMethodAsync(sln, methodSpec);
var unsafeParams = CollectUnsafeParameters(methodSymbol);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/TransformParametersCore.cs:54" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/TransformParametersCore.cs#L54">TransformParametersCore.cs:54-58</a> | TransformParametersCore | RunNewTypeAsync |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/TransformParametersCore.cs:96" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/TransformParametersCore.cs#L96">TransformParametersCore.cs:96-100</a> | TransformParametersCore | RunExistingTypeAsync |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | fail | block produces 2 outward variables; would need tuple return or out-params |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 36: GRP-3-6d98356b

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
var newlineIdx = first.IndexOfAny(['\r', '\n']);
if (newlineIdx >= 0)
    first = first[..newlineIdx].TrimEnd() + " …";
return first.Length > 120 ? first[..120] + " …" : first;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/FindDuplicatesCore.cs:395" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/FindDuplicatesCore.cs#L395">FindDuplicatesCore.cs:395-398</a> | FindDuplicatesCore | BuildPreview |
| 2 | <a href="vscode://file/C:/code/roslyn-tools/src/RoslynTools/Core/PatternMatcher.cs:293" title="file:///C:/code/roslyn-tools/src/RoslynTools/Core/PatternMatcher.cs#L293">PatternMatcher.cs:293-296</a> | PatternMatcher | BuildPreview |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

