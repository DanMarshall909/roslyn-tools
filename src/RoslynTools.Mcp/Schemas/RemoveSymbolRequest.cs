namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to safely delete a symbol after verifying it has zero references (with dry-run safety).
/// </summary>
public sealed record RemoveSymbolRequest(
    /// <summary>
    /// Symbol name or fully-qualified name (e.g., "MyClass" or "ProjectX.Models.MyClass").
    /// </summary>
    string Symbol,

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
