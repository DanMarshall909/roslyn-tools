---
title: Inspect code
description: Resolve symbols and inspect source, signatures, and bounded relationships without falling back to text search.
order: 4
section: Navigation
---

Inspection commands answer focused questions about symbols and source. They are read-only and return explicit identities that later operations can reuse.

## Resolve an unfamiliar symbol

Search for typed candidates when a short name is ambiguous:

```powershell
rt inspect search-symbols BookingService --solution MyApp.slnx --trust-repository --json
```

Candidate results include stable identity, kind, signature, project, namespace, and source location. Use the returned identity to avoid guessing between overloads or duplicate names.

## Describe and outline

`describe` focuses on one type or member:

```powershell
rt inspect describe BookingService --solution MyApp.slnx --trust-repository --json
```

`outline` gives a compact structural view of a file, glob, directory, project, or solution:

```powershell
rt inspect outline src/Booking/BookingService.cs --solution MyApp.slnx --trust-repository
rt inspect outline "src/**/*.cs" --solution MyApp.slnx --trust-repository --with-locations
```

## Read bounded source

Use `source` when you need a symbol's declaration and related declarations without reading an entire file tree:

```powershell
rt inspect source "M:MyApp.BookingService.ReserveAsync" \
  --solution MyApp.slnx --json
```

Source output reports total and returned byte counts so truncation remains visible.

## Traverse relationships

The relationship operation can inspect references, callers, implementations, and other bounded semantic edges:

```powershell
rt inspect relationships BookingService \
  --solution MyApp.slnx --json
```

Prefer semantic relationship results over textual matches when overloads, interfaces, inheritance, or extension methods matter. Keep the reported limitations when using the result as evidence.
