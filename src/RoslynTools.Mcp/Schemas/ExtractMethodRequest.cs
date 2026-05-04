namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to extract a statement range into a new private method (with dry-run safety).
/// </summary>
public sealed record ExtractMethodRequest(
    /// <summary>
    /// File path containing the method (e.g., "src/Services/MyService.cs").
    /// </summary>
    string File,

    /// <summary>
    /// Name for the new extracted method.
    /// </summary>
    string MethodName,

    /// <summary>
    /// Starting line number (1-based, inclusive).
    /// </summary>
    int StartLine,

    /// <summary>
    /// Ending line number (1-based, inclusive).
    /// </summary>
    int EndLine,

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
