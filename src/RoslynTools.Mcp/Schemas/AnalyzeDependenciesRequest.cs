namespace RoslynTools.Mcp.Schemas;

/// <summary>
/// Request to analyze circular namespace dependencies in the solution.
/// </summary>
public sealed record AnalyzeDependenciesRequest(
    /// <summary>
    /// Path to the solution file.
    /// </summary>
    string Solution);
