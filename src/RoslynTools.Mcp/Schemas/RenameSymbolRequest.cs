namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// A request class for renaming symbols in a Roslyn project.
/// </summary>
public sealed record RenameSymbolRequest(string OldName, string NewName, string Solution, bool Apply = false);
