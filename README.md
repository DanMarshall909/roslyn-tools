# roslyn-tools

A CLI for solution-wide C# refactoring powered by Roslyn. Rename symbols, move namespaces, find references and implementations — all resolved semantically across an entire `.sln`.

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

### Build

```sh
dotnet build RoslynTools.slnx
```

### Run

```sh
# Via dotnet run
dotnet run --project src/RoslynTools/RoslynTools.csproj -- <command> [options]

# Via wrapper scripts (after building)
rt.cmd <command> [options]        # CMD
.\rt.ps1 <command> [options]      # PowerShell
```

### Publish as single exe

```sh
dotnet publish src/RoslynTools/RoslynTools.csproj -c Release
# Output: src/RoslynTools/bin/Release/net8.0/win-x64/publish/rt.exe
```

## Commands

### rename

Rename a symbol and update all references across the solution.

```sh
rt rename <symbol> <new-name> [--solution <path>] [--kind <kind>] [--dry-run]
```

```sh
# Rename a class
rt rename "MyApp.Services.UserService" "AccountService"

# Rename a method
rt rename "MyApp.Helpers.StringUtil.Sanitize" "Clean" --kind method

# Preview without writing
rt rename "MyApp.Models.Order" "PurchaseOrder" --dry-run
```

### move

Move a type or entire namespace, updating declarations and all references.

```sh
# Single type
rt move --solution ./MySolution.sln --type "MyApp.Old.Customer" --to-namespace "MyApp.New" [--move-file] [--dry-run]

# Batch — all types in a namespace
rt move --solution ./MySolution.sln --namespace "MyApp.Old" --to-namespace "MyApp.New" [--move-file] [--dry-run]
```

`--move-file` relocates source files to match the new namespace folder structure.

### find-refs

Find all references to a symbol.

```sh
rt find-refs --solution ./MySolution.sln --symbol "BookingService" [--kind type]
```

Output:

```
C:\code\src\Services\BookingHandler.cs(14): var svc = new BookingService();
C:\code\src\Controllers\BookingController.cs(8): private readonly BookingService _svc;

Found 2 reference(s) to 'BookingService'.
```

### inspect

Show a type's full shape as JSON — members, signatures, base types, interfaces, locations. Designed for LLM agents to replace multiple file reads with a single call.

```sh
rt inspect "MyApp.Services.BookingService" [--solution <path>] [--kind <kind>]
```

```json
{
  "name": "BookingService",
  "kind": "NamedType",
  "fqn": "MyApp.Services.BookingService",
  "accessibility": "Public",
  "baseType": "BaseService",
  "interfaces": ["IBookingService"],
  "members": [
    { "name": "CreateAsync", "kind": "Method", "signature": "Task<HeroResult> CreateAsync(BookingDto dto)" },
    { "name": "CancelAsync", "kind": "Method", "signature": "Task<HeroResult> CancelAsync(int id)" }
  ],
  "locations": [{ "file": "C:\\code\\src\\Services\\BookingService.cs", "line": 12 }]
}
```

### outline

List all types and member signatures in a file or namespace as JSON.

```sh
# By file
rt outline "src/Models/Customer.cs" [--solution <path>]

# By namespace
rt outline "MyApp.Models" [--solution <path>]
```

### find-impl

Find all implementations of an interface or derived classes of an abstract type. Output is JSON.

```sh
rt find-impl "MyApp.Interfaces.IRepository" [--solution <path>]
```

```json
{
  "symbol": "MyApp.Interfaces.IRepository",
  "symbolKind": "Interface",
  "implementationCount": 2,
  "implementations": [
    { "name": "SqlRepository", "fqn": "MyApp.Data.SqlRepository", "locations": [{ "file": "...", "line": 5 }] },
    { "name": "InMemoryRepository", "fqn": "MyApp.Data.InMemoryRepository", "locations": [{ "file": "...", "line": 3 }] }
  ]
}
```

## Common options

| Option | Description |
|---|---|
| `--solution <path>` | Path to `.sln` file. Auto-detected if there's exactly one in the current directory. |
| `--kind <kind>` | Filter by symbol kind: `type`, `method`, `property`, `field`, `namespace` |
| `--dry-run` | Preview changes without writing to disk |

## Tests

```sh
dotnet test RoslynTools.slnx -v minimal
```

Each test generates a temporary C# project, builds it, runs Roslyn operations against it, and cleans up automatically.
