namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to fix/correct a file's namespace declaration to match its folder path (with dry-run safety).
/// </summary>
public sealed record FixNamespaceRequest(
    /// <summary>
    /// File path to fix (e.g., "src/Services/MyService.cs").
    /// </summary>
    string File,

    /// <summary>
    /// Path to the solution file.
    /// </summary>
    string Solution,

    /// <summary>
    /// If true, preview changes without writing (default: true).
    /// </summary>
    bool DryRun = true,

    /// <summary>
    /// If true, return a unified diff (default: true).
    /// </summary>
    bool ReturnDiff = true,

    /// <summary>
    /// If true, apply changes. Writes only when Apply=true AND DryRun=false (default: false).
    /// </summary>
    bool Apply = false);
