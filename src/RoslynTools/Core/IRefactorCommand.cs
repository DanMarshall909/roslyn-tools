namespace RoslynTools.Core;

/// <summary>
///     Marker for refactor-category commands (mutations). Enforced to imply
///     <see cref="RtCommandInfo.Category" /> == <c>"refactor"</c>,
///     <see cref="RtCommandInfo.Mutates" /> == <c>true</c>, must also implement
///     <see cref="IMutatingCommand" />, and <c>Build()</c> must register a <c>--dry-run</c> option.
/// </summary>
internal interface IRefactorCommand : IRtCommand { }
