namespace RoslynTools.Core;

/// <summary>
///     Marker for analyze-category commands (read-only, cross-cutting solution scans).
///     Enforced to imply <see cref="RtCommandInfo.Category" /> == <c>"analyze"</c>,
///     <see cref="RtCommandInfo.Mutates" /> == <c>false</c>,
///     <see cref="RtCommandInfo.JsonOutput" /> == <c>true</c>.
/// </summary>
internal interface IAnalyzeCommand : IRtCommand;
