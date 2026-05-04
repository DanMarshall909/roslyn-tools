namespace RoslynTools.Mcp.Schemas;

public sealed record RenameSymbolRequest(string OldName, string NewName, string Solution, bool Apply = false);
