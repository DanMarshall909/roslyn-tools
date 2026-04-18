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

### Mutation — symbol & namespace

| Command | Alias | Description |
|---------|-------|-------------|
| `rename` | `ren` | Rename a symbol and update all references |
| `move` | | Move a type or namespace, updating declarations and references |
| `fix-namespace` | `fns` | Align a file's namespace declaration to its folder path |

### Mutation — structure

| Command | Alias | Description |
|---------|-------|-------------|
| `extract-interface` | `ei` | Generate `IXxx` from a class's public members |
| `extract-method` | `em` | Extract statements into a new private method |
| `extract-class` | `ec` | Move named members into a new class file |
| `add-dependency` | `ad` | Inject a constructor dependency (field + param + assignment) |
| `replace-datetime` | | Replace `DateTime.UtcNow/Now` with injected `TimeProvider` |
| `make-async` | `ma` | Convert a method to `async Task<T>` and update direct callers |

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

### extract-interface

```sh
rt extract-interface --type AgentService --solution MySolution.sln [--dry-run]
# Creates IAgentService.cs alongside AgentService.cs
# Updates AgentService to implement IAgentService
```

### add-dependency

```sh
rt add-dependency --type AgentService --dependency IEmailService --solution MySolution.sln
# Adds: private readonly IEmailService _emailService;
# Adds: IEmailService emailService parameter to constructor
# Adds: _emailService = emailService; assignment
```

### find-unused

```sh
rt find-unused --solution MySolution.sln --scope methods
# method     OldHelper.Calculate   src/Helpers/OldHelper.cs:14
```

### extract-class

```sh
rt extract-class --type BigService --members "LogInfo,LogError" --new-type LogHelper --solution MySolution.sln [--dry-run]
# Creates LogHelper.cs alongside BigService.cs
# Removes LogInfo, LogError from BigService
# Injects LogHelper into BigService constructor
```

### make-async

```sh
rt make-async --type AgentService --method GetAgent --solution MySolution.sln [--dry-run]
# Renames GetAgent -> GetAgentAsync
# Wraps return type: string -> Task<string>
# Updates direct callers with await
```

### fix-namespace

```sh
rt fix-namespace --file src/Services/Agents/AgentService.cs --solution MySolution.sln
# Updates namespace declaration to MyApp.Services.Agents
# Updates using statements in referencing files
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

Each test generates a throwaway C# project, builds it, runs Roslyn operations against it, and cleans up automatically. 80 tests total.
