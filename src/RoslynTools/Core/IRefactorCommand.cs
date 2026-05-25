namespace RoslynTools.Core;

/// <summary>
///     Marker for refactor-category commands (mutations). Subtype of
///     <see cref="IMutatingCommand" /> — every refactor IS a mutation, so the
///     capability is inherited rather than declared per-command. Enforced to imply
///     <see cref="RtCommandInfo.Category" /> == <c>"refactor"</c>,
///     <see cref="RtCommandInfo.Mutates" /> == <c>true</c>, and <c>Build()</c> must
///     register a <c>--dry-run</c> option (Batch 4 architecture test).
/// </summary>
internal interface IRefactorCommand : IMutatingCommand;
