namespace RoslynTools.Core;

/// <summary>
///     Marker interface for <c>inspect</c> and <c>analyze</c> commands that emit a JSON array to
///     stdout when <c>--json</c> is passed. Must be implemented in lock-step with
///     <see cref="RtCommandInfo.JsonOutput" /> = <c>true</c> — <see cref="RoslynTools.App" />'s
///     static constructor asserts both are set together, catching mismatches at startup.
/// </summary>
internal interface IStructuredOutputCommand { }
