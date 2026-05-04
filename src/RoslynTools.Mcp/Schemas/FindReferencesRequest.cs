namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to find all references to a symbol across the solution.
/// </summary>
public sealed record FindReferencesRequest(
    /// <summary>
    /// Symbol name or fully-qualified name (e.g., "MyClass" or "ProjectX.Services.MyClass").
    /// </summary>
    string Symbol,

    /// <summary>
    /// Path to the solution file (e.g., "Foo.sln" or absolute path).
    /// </summary>
    string Solution,

    /// <summary>
    /// Scope filter: "public", "internal", "private", or "all" (default: "all").
    /// </summary>
    string Scope = "all",

    /// <summary>
    /// Include test code in search (default: true).
    /// </summary>
    bool IncludeTests = true);
