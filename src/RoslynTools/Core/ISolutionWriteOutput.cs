namespace RoslynTools.Core;

/// <summary>Receives progress and preview lines from solution writes.</summary>
internal interface ISolutionWriteOutput
{
    /// <summary>Reports a result line for the calling host to render.</summary>
    void Result(string message);

    /// <summary>Reports a diff line for the calling host to render.</summary>
    void Diff(string message);

    /// <summary>Reports a document change for host-owned diff rendering.</summary>
    void DocumentDiff(string oldText, string newText, string filePath);

    /// <summary>Reports a warning for the calling host to render.</summary>
    void Warning(string message);
}

/// <summary>Suppresses write reports when a Core caller has no output host.</summary>
internal sealed class NullSolutionWriteOutput : ISolutionWriteOutput
{
    internal static readonly NullSolutionWriteOutput Instance = new();

    /// <summary>Discards a result line.</summary>
    public void Result(string message)
    {
    }

    /// <summary>Discards a diff line.</summary>
    public void Diff(string message)
    {
    }

    /// <summary>Discards a document preview.</summary>
    public void DocumentDiff(string oldText, string newText, string filePath)
    {
    }

    /// <summary>Discards a warning.</summary>
    public void Warning(string message)
    {
    }
}
