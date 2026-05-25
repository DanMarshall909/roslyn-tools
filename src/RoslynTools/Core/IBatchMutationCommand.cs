namespace RoslynTools.Core;

/// <summary>
///     Capability marker: uses the <c>BatchMutationRunner</c> / <c>FixId</c> pipeline
///     (see commit <c>94c0813</c>). ZERO production adopters today — defined as a ratchet
///     for future batch-mutator commands. Architecture test must tolerate zero adopters.
/// </summary>
internal interface IBatchMutationCommand : IMutatingCommand { }
