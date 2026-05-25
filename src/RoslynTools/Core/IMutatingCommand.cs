namespace RoslynTools.Core;

/// <summary>
///     Capability marker: produces file changes. Must expose a <c>--dry-run</c> option and emit
///     a diff or change summary. Architecture test (Batch 4) asserts every
///     <see cref="IRefactorCommand" /> is also <see cref="IMutatingCommand" />.
/// </summary>
internal interface IMutatingCommand : IRtCommand;
