namespace RoslynTools.Core.Output;

/// <summary>
/// Context class for managing structured output from Roslyn.
/// </summary>
internal static class RtContext
{
    public static OutputFormat Format { get; set; } = OutputFormat.Text;
    public static OutputLevel Level { get; set; } = OutputLevel.Default;

    /// <summary>
    ///     When <c>true</c>, the converged JSON output paths (<see cref="StructuredOutput.Write" />,
    ///     <see cref="Renderers.JsonRenderer.Write" />, MCP DescribeHandler / OutlineHandler) emit raw
    ///     non-ASCII and HTML-sensitive characters via
    ///     <see cref="System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping" />.
    ///     Default <c>false</c> keeps the strict-escape behaviour required by strict JSON deserialisers.
    ///     Set by the global <c>--relax-json</c> flag.
    /// </summary>
    public static bool RelaxJson { get; set; }

    /// <summary>
    ///     When true, MSBuild workspace warnings (NuGet audit, unresolved imports, etc.) are emitted to stderr.
    ///     Default is false so routine warning noise does not pollute rt output. Set by --show-workspace-warnings.
    ///     Workspace failures (Kind == Failure) are always emitted regardless of this flag.
    /// </summary>
    public static bool ShowWorkspaceWarnings { get; set; }

    /// <summary>
    ///     When true, suppress the "Did you mean: ..." tail appended to drill-in not-found error messages
    ///     (e.g. <c>rt inspect describe Type.Misspelt(int)</c>). Default false → suggestions ON because the
    ///     tail goes to stderr alongside the error message and never pollutes structured stdout output.
    ///     Set by the global <c>--no-suggest</c> flag for strict-output pipelines that pin error text.
    /// </summary>
    public static bool NoSuggest { get; set; }
}
