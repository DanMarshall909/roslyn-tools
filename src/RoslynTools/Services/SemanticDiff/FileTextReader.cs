namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     The real <see cref="IFileTextReader" />: reads file text via <c>System.IO.File</c>.
///     A missing file returns <c>null</c> (the caller turns that into a clean error); any
///     other read failure is re-thrown as an <see cref="IOException" /> carrying the path,
///     which the CLI's user-error handler renders as "Error: ..." + exit 1 rather than a
///     stack trace.
/// </summary>
internal sealed class FileTextReader : IFileTextReader
{
    public string? TryReadAllText(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is not IOException)
        {
            // Normalise non-IO read failures (e.g. UnauthorizedAccessException) to IOException
            // so they land on the CLI's clean user-error branch instead of being captured as
            // an unhandled exception. A genuine IOException already lands there, so let it pass.
            throw new IOException($"could not read file: {path} ({ex.Message})", ex);
        }
    }
}
