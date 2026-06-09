namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Narrow abstraction over reading a file's text from disk, scoped to exactly what the
///     two-files-on-disc diff mode needs. Exists so <see cref="FileSemanticDiffSourceProvider" />
///     can be unit-tested with a fake — no real files required. The one real implementation,
///     <see cref="FileTextReader" />, reads via <c>System.IO.File</c>.
/// </summary>
internal interface IFileTextReader
{
    /// <summary>
    ///     Return the full text of the file at <paramref name="path" />, or <c>null</c> when the
    ///     file does not exist. A read that fails for any other reason (locked, permission
    ///     denied) throws <see cref="IOException" /> so the caller can surface a clean error;
    ///     it never bubbles up as an unhandled exception.
    /// </summary>
    string? TryReadAllText(string path);
}
