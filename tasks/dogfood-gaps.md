- 2026-04-17: Replace Console.Error.WriteLine with ILogger across multiple partial controller files — rt has no general pattern-replace command (only replace-datetime is supported)

## inline-method known issues (US-002, 2026-04-17)
- Method removal after inlining scopes by identifier text only — will remove wrong method if same name appears in different class in same file. Fix: scope to containing TypeDeclarationSyntax by type name + param count.
- ParameterSubstitutor does not parenthesize substituted arguments — `Double(a+b)` inlines as `a+b*2` not `(a+b)*2`. Fix: wrap non-trivial argument expressions in ParenthesizedExpressionSyntax.
