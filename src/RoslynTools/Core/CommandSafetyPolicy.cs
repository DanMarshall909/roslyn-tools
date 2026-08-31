namespace RoslynTools.Core;

/// <summary>
///     Host-neutral safety policy used while legacy descriptors are projected into host tools.
/// </summary>
internal enum CommandSafetyPolicy
{
    ReadOnly,
    DryRunByDefault,
    LocalWrite,
    ExternalWriteReviewByDefault
}
