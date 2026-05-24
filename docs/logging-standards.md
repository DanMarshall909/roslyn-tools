# Logging and Output Standards

Production code under `src/` does not call `System.Console.*` directly. Every
write goes through `RtConsole` or `RtLog`, both of which are intercepted in
daemon mode and route to the appropriate stream (stdout vs stderr) based on
the context flag. The architectural lock lives in
`tests/RoslynTools.Tests/Architecture/NoDirectConsoleUse.cs` — a single
allowed-list (`RtConsole.cs`, `IRtConsole.cs`) covers the bridge layer; any
other production file that references `Console.<member>` fails the build.

## Rules

1. Production console I/O should go through `RtConsole`.
2. Diagnostic log calls should use message templates, not dynamic expressions.
3. Command result payloads belong on stdout; diagnostics and progress belong on stderr.
4. Dry-run unified diffs belong on stdout.

## Templates

Use literal templates with named placeholders:

```csharp
RtLog.Info("Updated {FilePath}", filePath);
RtLog.Warning("Skipped {Count} document(s) with no file path.", count);
```

Avoid interpolated strings, concatenation, variables, and `string.Format` for log templates:

```csharp
RtLog.Info($"Updated: {filePath}");
RtLog.Info("Updated: " + filePath);
RtLog.Info(message);
```

## Output Contexts

Use `RtConsole` with the appropriate `RtOutputContext` flag.

| Context | Stream | Use for |
| --- | --- | --- |
| `Diagnostic` | stderr | Operational messages: progress, warnings, errors |
| `Result` | stdout | Command result data: JSON payloads, plain-text summaries |
| `Diff` | stdout | Dry-run unified diff output |
| `Interactive` | stdout | Disambiguation prompts, numbered menus |
| `Ui` | stdout | Decorative terminal output: colors, section headers |

If the `Diagnostic` flag is set, `RtConsole` routes to stderr. Other contexts route to stdout.
