namespace RoslynTools.Core;

internal interface IRtLogger
{
    void Log(RtLogLevel level, string message);
}
