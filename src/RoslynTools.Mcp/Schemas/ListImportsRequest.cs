namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to list all C# using directives (imports) in the solution.
/// </summary>
public sealed record ListImportsRequest(
    /// <summary>
    /// Path to the solution file.
    /// </summary>
    string Solution,

    /// <summary>
    /// Include global using directives (default: true).
    /// </summary>
    bool IncludeGlobal = true,

    /// <summary>
    /// Include using static directives (default: true).
    /// </summary>
    bool IncludeStatic = true);
