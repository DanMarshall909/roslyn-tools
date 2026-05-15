namespace RoslynTools.Core.Output;

/// <summary>
/// Context class for managing structured output from Roslyn.
/// </summary>
internal static class RtContext
{
    public static OutputFormat Format { get; set; } = OutputFormat.Text;
    public static OutputLevel Level { get; set; } = OutputLevel.Default;

    /// <summary>
    ///     When true, JSON output uses <see cref="System.Text.Encodings.Web.JavaScriptEncoder.Default" />
    ///     so all non-ASCII and control characters are escaped as \uXXXX.  Set by --strict-json.
    /// </summary>
    public static bool StrictJson { get; set; }

    /// <summary>
    ///     When true, MSBuild workspace warnings (NuGet audit, unresolved imports, etc.) are emitted to stderr.
    ///     Default is false so routine warning noise does not pollute rt output. Set by --show-workspace-warnings.
    ///     Workspace failures (Kind == Failure) are always emitted regardless of this flag.
    /// </summary>
    public static bool ShowWorkspaceWarnings { get; set; }
}
