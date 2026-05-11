namespace RoslynTools.Core.Decisions;

/// <summary>
/// DecisionsRequiredException
/// </summary>
internal sealed class DecisionsRequiredException : Exception
{
    public DecisionsRequiredException()
    {
    }

    public DecisionsRequiredException(string message) : base(message)
    {
    }

    public DecisionsRequiredException(string message, Exception inner) : base(message, inner)
    {
    }
}
