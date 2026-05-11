namespace RoslynTools.Core.Output;

internal static class RtContext
{
    public static OutputFormat Format { get; set; } = OutputFormat.Text;
    public static OutputLevel Level { get; set; } = OutputLevel.Default;

    /// <summary>
    ///     When true, JSON output uses <see cref="System.Text.Encodings.Web.JavaScriptEncoder.Default" />
    ///     so all non-ASCII and control characters are escaped as \uXXXX.  Set by --strict-json.
    /// </summary>
    public static bool StrictJson { get; set; }
}
