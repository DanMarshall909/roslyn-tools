namespace RoslynTools.Core;

internal static class PreviewFormatter
{
    internal static string TruncatePreviewLine(string first)
    {
        var newlineIdx = first.IndexOfAny(['\r', '\n']);
        if (newlineIdx >= 0)
            first = first[..newlineIdx].TrimEnd() + " …";
        return first.Length > 120 ? first[..120] + " …" : first;
    }
}
