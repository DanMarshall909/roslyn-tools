namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to convert a synchronous method to async Task&lt;T&gt; (with dry-run safety).
/// </summary>
public sealed record MakeAsyncRequest(
    /// <summary>
    /// Type name containing the method (e.g., "MyClass").
    /// </summary>
    string Type,

    /// <summary>
    /// Method name to convert (e.g., "ProcessRequest").
    /// </summary>
    string Method,

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
