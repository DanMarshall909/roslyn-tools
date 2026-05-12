namespace RoslynTools.Core;

internal static class FilePathFilters
{
    internal static bool IsGeneratedOrIntermediate(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }
}
