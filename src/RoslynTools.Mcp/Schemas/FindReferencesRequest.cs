namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// A request class for finding references in a Roslyn project.
/// </summary>
public sealed record FindReferencesRequest(string Symbol, string Solution);
