namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to rename a C# symbol and update all references (with dry-run safety).
/// </summary>
public sealed record RenameSymbolRequest(
    /// <summary>
    /// Old symbol name.
    /// </summary>
    string OldName,

    /// <summary>
    /// New symbol name.
    /// </summary>
    string NewName,

    /// <summary>
    /// Path to the solution file.
    /// </summary>
    string Solution,

    /// <summary>
    /// Scope filter: "public", "internal", "private", or "all" (default: "all").
    /// </summary>
    string Scope = "all",

    /// <summary>
    /// If true, preview changes without writing to disk (default: true for safety).
    /// </summary>
    bool DryRun = true,

    /// <summary>
    /// If true, return a unified diff of changes (default: true).
    /// </summary>
    bool ReturnDiff = true,

    /// <summary>
    /// If true, apply changes to disk. Only writes when Apply=true AND DryRun=false (default: false for safety).
    /// </summary>
    bool Apply = false);
