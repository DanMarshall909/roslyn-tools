namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to find all invocation sites (callers) of a method.
/// </summary>
public sealed record FindCallersRequest(
    /// <summary>
    /// Method name or fully-qualified name (e.g., "SendEmail" or "MyClass.SendEmail").
    /// </summary>
    string Symbol,

    /// <summary>
    /// Path to the solution file.
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
