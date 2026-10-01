namespace RoslynTools.Services.SemanticDiff.Git;

/// <summary>A Git or working-tree content read failed without proof of absence.</summary>
internal sealed class GitContentReadException : InvalidOperationException
{
    internal GitContentReadException()
    {
    }

    internal GitContentReadException(string message) : base(message)
    {
    }

    internal GitContentReadException(string message, Exception innerException) : base(message, innerException)
    {
    }

    internal GitContentReadException(
        string reference,
        string path,
        string message,
        Exception innerException) : base(message, innerException)
    {
        Reference = reference;
        Path = path;
    }

    internal string Reference { get; } = string.Empty;

    internal string Path { get; } = string.Empty;
}
