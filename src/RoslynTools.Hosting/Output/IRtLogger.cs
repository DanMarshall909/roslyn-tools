namespace RoslynTools.Core;

/// <summary>
/// An interface for a logger output system.
/// </summary>
internal interface IRtLogger
{
    void Log(RtLogLevel level, string message);
}
