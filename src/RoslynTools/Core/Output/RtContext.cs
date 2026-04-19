namespace RoslynTools.Core.Output;

internal static class RtContext
{
    public static OutputFormat Format { get; set; } = OutputFormat.Text;
    public static OutputLevel Level { get; set; } = OutputLevel.Default;
}
