# roslyn-tools

A CLI for solution-wide C# refactoring and analysis powered by Roslyn. Rename symbols, extract interfaces, inject dependencies, find dead code — all resolved semantically across an entire `.sln`.

## Install

```sh
dotnet tool install -g roslyn-tools-rt
```

Requires .NET 8 SDK or later. After install, `rt` is on your PATH.

## Build from source

```sh
dotnet build RoslynTools.slnx

# Run without installing
dotnet run --project src/RoslynTools/RoslynTools.csproj -- <command> [options]

# Publish as single exe (Windows)
dotnet publish src/RoslynTools/RoslynTools.csproj -c Release -r win-x64
```

## Commands

### Navigation & inspection

| Command | Alias | Description |
|---------|-------|-------------|
| `find-refs` | | Find all references to a symbol |
| `find-impl` | `impls` | Find all implementations of an interface or abstract class |
| `find-callers` | `callers` | Find all invocation sites for a method (not type refs, not `nameof`) |
| `find-pattern` | `fp` | Find all locations structurally matching a given statement range (same SyntaxKind shape, identifiers/literals may differ) |
| `find-unused` | `unused` | Report types and methods with zero references |
| `find-duplicates` | `fd` | Report exact-clone code blocks repeated across the solution |
| `inspect` | | Show type shape as JSON — members, bases, interfaces, locations |
| `outline` | | List all types/members in a file or namespace as JSON |
| `list-smells` | `smells` | Report large classes, long methods, over-injected constructors |
| `circular-deps` | `cdeps` | Detect circular namespace dependencies |
| `dependency-graph` | `depgraph` | Output a type dependency graph as JSON or DOT |

### Mutation — symbol & namespace

| Command | Alias | Description |
|---------|-------|-------------|
| `rename` | `ren` | Rename a symbol and update all references |
| `move` | | Move a type or namespace, updating declarations and references |
| `fix-namespace` | `fns` | Align a file's namespace declaration to its folder path |
| `replace-call` | `rc` | Replace all invocations of one method with another |
| `safe-delete` | `sd` | Delete a type or member only if it has zero references |
| `transform-parameters` | `tp` | Extract method parameters into a new record and update all call sites |

### Mutation — structure

| Command | Alias | Description |
|---------|-------|-------------|
| `extract-interface` | `ei` | Generate `IXxx` from a class's public members |
| `extract-method` | `em` | Extract statements into a new private method |
| `extract-class` | `ec` | Move named members into a new class file |
| `inline-method` | `im` | Inline all call sites of a method and delete it |
| `add-dependency` | `ad` | Inject a constructor dependency (field + param + assignment) |
| `add-null-checks` | `anc` | Insert `ArgumentNullException.ThrowIfNull` guards for reference params |
| `replace-datetime` | | Replace `DateTime.UtcNow/Now` with injected `TimeProvider` |
| `make-async` | `ma` | Convert a method to `async Task<T>` and update direct callers |
| `make-sealed` | `ms` | Add `sealed` to a class if no derived types exist |
| `make-static` | `mst` | Convert an instance method to static when it has no instance-state dependencies |
| `make-instance` | `mns` | Remove `static` from a method and rewrite call sites to use a reachable instance |
| `convert-to-record` | `ctr` | Convert a data class to a C# record |
| `implement-interface` | `ii` | Add stub implementations for unimplemented interface members |

### Testing

| Command | Alias | Description |
|---------|-------|-------------|
| `add-test` | `at` | Add xUnit test stubs by name or generate from acceptance criteria |

---

## Examples

### find-callers

```sh
rt find-callers --symbol "AgentService.GetAgent" --solution MySolution.sln
# src/Services/AgentService.cs:42: GetAll: var result = _repo.GetAgent(id);
```

### list-smells

```sh
rt list-smells --solution MySolution.sln --max-method-lines 30
# LargeClass          AdminController   src/Controllers/AdminController.cs:1  (524 lines)
# LongMethod          GetAll            src/Controllers/AdminController.cs:44  (67 lines)
# TooManyDependencies AdminController   src/Controllers/AdminController.cs:1  ctor (9 params)
```

### inline-method

```sh
rt inline-method --type MathHelper --method Double --solution MySolution.sln [--dry-run]
# Replaces every call to Double(x) with x * 2 and removes the method
```

### safe-delete

```sh
rt safe-delete --symbol OldHelper --solution MySolution.sln
# Deletes OldHelper if zero references; lists callers and exits 1 if referenced
```

### replace-call

```sh
rt replace-call --from "Logger.Log" --to "LogInfo" --solution MySolution.sln [--dry-run]
# All Logger.Log(...) calls become Logger.LogInfo(...)
```

### add-null-checks

```sh
rt add-null-checks --type OrderService --method Process --solution MySolution.sln [--dry-run]
# Inserts ArgumentNullException.ThrowIfNull(name); for each non-nullable reference param
```

### make-sealed

```sh
rt make-sealed --type PaymentProcessor --solution MySolution.sln [--dry-run]
# Adds 'sealed' if no derived types found; lists them and exits 1 if any exist
```

### transform-parameters

```sh
# Generate a new record
rt transform-parameters --method OrderService.PlaceOrder --new-type PlaceOrderArgs --solution MySolution.sln [--dry-run]
# Generates:  internal sealed record PlaceOrderArgs(string CustomerId, string ProductId, int Quantity);
# Rewrites:   PlaceOrder(string customerId, ...) → PlaceOrder(PlaceOrderArgs args)
# Updates:    svc.PlaceOrder("c1", "p1", 3) → svc.PlaceOrder(new PlaceOrderArgs("c1", "p1", 3))

# With default value preservation:
rt tp --method Greeter.Greet --new-type GreetArgs --solution MySolution.sln
# Generates:  internal sealed record GreetArgs(string Name, int Times = 1);

# Reuse an existing record (positional constructor at call sites):
rt transform-parameters --method OrderService.PlaceOrder --existing-type MyApp.PlaceOrderArgs --solution MySolution.sln [--dry-run]
# No new type generated. Validates that PlaceOrderArgs has matching record parameters for every method param.
# Updates:    svc.PlaceOrder("c1", "p1", 3) → svc.PlaceOrder(new PlaceOrderArgs("c1", "p1", 3))

# Reuse an existing class (object initializer at call sites):
rt tp --method SearchService.Search --existing-type MyApp.SearchArgs --solution MySolution.sln
# SearchArgs must have public properties Query and MaxResults.
# Updates:    svc.Search("hello", 10) → svc.Search(new SearchArgs { Query = "hello", MaxResults = 10 })
```

### make-static

```sh
rt make-static --method MathHelper.Clamp --solution MySolution.sln [--dry-run]
# Validates: no 'this', no instance field/property access, not virtual/override/abstract
# Adds 'static' modifier; rewrites instance.Clamp(...) call sites to MathHelper.Clamp(...)
# Exits 1 with a clear message if any blocking usage is found
```

### make-instance

```sh
rt make-instance --method OrderService.Validate --solution MySolution.sln [--dry-run]
# Removes 'static' from the method declaration.
# Rewrites Type.Method(...) call sites to instance.Method(...) when an instance is reachable:
#   - local variable of the type → var svc = new OrderService(); ... svc.Validate(...)
#   - constructor/method parameter of the type → void Handle(OrderService svc) { svc.Validate(...) }
#   - injected field of the type → _orderService.Validate(...)
#   - same-class instance context → this.Validate(...)
# Emits a WARNING with file:line for call sites with no reachable instance and leaves them unchanged.
# Prints a summary: N call sites found, M rewritten, K skipped.
```

### convert-to-record

```sh
rt convert-to-record --type Money --solution MySolution.sln [--dry-run]
# class Money { public decimal Amount { get; init; } ... }
# → record Money(decimal Amount, ...)
```

### implement-interface

```sh
rt implement-interface --type Repo --interface IRepository --solution MySolution.sln [--dry-run]
# Adds 'throw new NotImplementedException()' stubs for all unimplemented members
```

### find-duplicates

Scan the entire solution for exact-clone statement blocks (verbatim syntax, trivia stripped). Useful before an extract-method pass to discover the highest-value refactoring targets.

```sh
# Report all duplicated blocks of 3+ statements appearing 2+ times
rt find-duplicates --solution MySolution.sln

# Raise the bar: only blocks of 5+ statements, 3+ occurrences
rt fd --min-statements 5 --min-occurrences 3 --solution MySolution.sln
```

Output (plain text to stdout):

```text
Group 1  occurrences=3  score=9
preview: int x = 1;
  1. src/Foo.cs:10 in MyClass.MyMethod
  2. src/Bar.cs:20 in OtherClass.OtherMethod
  3. src/Baz.cs:30 in AnotherClass.AnotherMethod
```

### find-pattern

Find every place in the solution that structurally matches a given range of statements. The match is purely structural — same `SyntaxKind` tree shape — so different variable names and literal values are allowed.

```sh
# Find all code that structurally matches lines 42–50 of BookingService.cs
rt find-pattern --file src/Services/BookingService.cs --start-line 42 --end-line 50 --solution MySolution.sln

# Narrow to a specific namespace
rt fp --file src/Services/BookingService.cs --start-line 42 --end-line 50 \
  --scope MyApp.Services --solution MySolution.sln
```

Output (JSON to stdout):

```json
{
  "matches": [
    {
      "file": "src/Services/PaymentService.cs",
      "line": 88,
      "endLine": 96,
      "substitutions": { "booking": "payment", "bookingId": "paymentId" },
      "preview": "var result = _repo.Get(payment);"
    }
  ]
}
```

Use `--scope <namespace-prefix>` to restrict results to documents in a specific namespace or project.

### circular-deps

```sh
rt circular-deps --scope MyApp --solution MySolution.sln
# MyApp.Services -> MyApp.Data -> MyApp.Services  (cycle found)
# No circular namespace dependencies found.
```

### dependency-graph

```sh
rt dependency-graph --type OrderService --depth 2 --format dot --solution MySolution.sln
# digraph { "OrderService" -> "IOrderRepo"; "OrderService" -> "IEmailService"; ... }
```

### add-test

```sh
# Single test
rt add-test --class OrderServiceTests --name "PlaceOrder_Throws_WhenOutOfStock" --solution MySolution.sln

# Generate from ACs
rt add-test --class OrderServiceTests \
  --acs "Returns null when id not found; Throws when quantity is negative; Succeeds with valid input" \
  --nested-class "GivenValidOrder" \
  --solution MySolution.sln
# Generates:
#   public class GivenValidOrder {
#     [Fact] public void Returns_Null_When_Id_Not_Found() { throw new NotImplementedException(); }
#     [Fact] public void Throws_When_Quantity_Is_Negative() { throw new NotImplementedException(); }
#     [Fact] public void Succeeds_With_Valid_Input() { throw new NotImplementedException(); }
#   }
```

### extract-method

Extract a consecutive range of statements into a new private method. Uses Roslyn data-flow analysis to infer parameters and return type automatically.

```sh
# Basic extraction
rt extract-method src/Services/OrderService.cs ValidateOrder --start-line 55 --end-line 68 --solution MySolution.sln

# Preview without writing
rt em src/Controllers/AdminController.cs BuildCsvRow --start-line 110 --end-line 125 --dry-run

# Extract and replace all structural duplicates across the solution in one operation
rt extract-method src/Services/BookingService.cs ApplyDiscount --start-line 42 --end-line 50 \
  --replace-duplicates --solution MySolution.sln
# Extracts ApplyDiscount, then scans the solution for structurally-matching code.
# Each match is rewritten to a call to ApplyDiscount(arg1, arg2, ...).
# Emits a warning and skips any match where an argument can't be cleanly mapped.
# Prints a summary: matches found: N, replaced: M, skipped: K

# Dry-run with --replace-duplicates: shows all proposed replacements as a unified diff
rt em src/Services/BookingService.cs ApplyDiscount --start-line 42 --end-line 50 \
  --replace-duplicates --dry-run --solution MySolution.sln
```

### extract-interface / add-dependency / find-unused / extract-class / make-async / fix-namespace

```sh
rt extract-interface --type AgentService --solution MySolution.sln [--dry-run]
rt add-dependency --type AgentService --dependency IEmailService --solution MySolution.sln
rt find-unused --solution MySolution.sln --scope methods
rt extract-class --type BigService --members "LogInfo,LogError" --new-type LogHelper --solution MySolution.sln [--dry-run]
rt make-async --type AgentService --method GetAgent --solution MySolution.sln [--dry-run]
rt fix-namespace --file src/Services/Agents/AgentService.cs --solution MySolution.sln
```

### rename / move / find-refs / inspect / outline / find-impl

```sh
rt rename "MyApp.Services.UserService" "AccountService" --dry-run
rt move --type "MyApp.Old.Customer" --to-namespace "MyApp.New" --move-file
rt find-refs --symbol "BookingService"
rt inspect "MyApp.Services.BookingService"
rt outline "src/Models/Customer.cs"
rt find-impl "MyApp.Interfaces.IRepository"
```

---

## AI agent skill

`docs/use-rt-skill.md` is a compact reference for all 31 commands — decision guide, flag summary, and output conventions — sized for an agent context window. Every Debug build automatically deploys it to `~/.claude/skills/use-rt/SKILL.md` so Claude Code picks it up as the `/use-rt` skill.

To deploy manually or to a different destination:

```sh
# Default: ~/.claude/skills/use-rt
.\scripts\deploy-skill.ps1

# Custom destination (OpenAI plugin dir, etc.)
.\scripts\deploy-skill.ps1 -DestDir "$env:USERPROFILE\openai-plugins\use-rt"
```

Skip auto-deploy on build: `dotnet build RoslynTools.slnx -p:DeployRtSkill=false`

---

## Common options

| Option | Description |
|--------|-------------|
| `--solution <path>` | Path to `.sln` file. Auto-detected if exactly one exists in the current directory. |
| `--dry-run` | Preview changes as a unified diff without writing to disk |
| `--kind <kind>` | Filter by symbol kind: `type`, `method`, `property`, `field`, `namespace` |

## Tests

```sh
dotnet test RoslynTools.slnx -v minimal
```

129 tests. Each test generates a throwaway C# project, runs Roslyn operations against it, and cleans up automatically.
