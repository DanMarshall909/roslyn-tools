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
| `find-unused` | `unused` | Report types and methods with zero references |
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

## Common options

| Option | Description |
|--------|-------------|
| `--solution <path>` | Path to `.sln` file. Auto-detected if exactly one exists in the current directory. |
| `--dry-run` | Preview changes as a unified diff without writing to disk |
| `--kind <kind>` | Filter by symbol kind: `type`, `method`, `property`, `field`, `namespace` |

## Tests

```sh
dotnet test RoslynTools.sln -v minimal
```

129 tests. Each test generates a throwaway C# project, runs Roslyn operations against it, and cleans up automatically.
