namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to get detailed structure information about a type.
/// </summary>
public sealed record ShowTypeRequest(
    /// <summary>
    /// Type name or fully-qualified name (e.g., "MyClass" or "ProjectX.Models.MyClass").
    /// </summary>
    string Symbol,

    /// <summary>
    /// Path to the solution file.
    /// </summary>
    string Solution);
