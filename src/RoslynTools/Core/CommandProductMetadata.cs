namespace RoslynTools.Core;

internal enum CapabilityMaturity
{
    StableCandidate,
    Preview,
    Experimental
}

internal enum CommandIdempotency
{
    Idempotent,
    Conditional,
    Unknown
}

internal enum WorkspaceRequirement
{
    None,
    ParseOnly,
    Semantic,
    CompleteSemantic
}

internal enum TrustRequirement
{
    None,
    ExplicitTrustedRepository
}

internal enum CommandCost
{
    Low,
    Medium,
    High
}

internal enum CancellationSupport
{
    Cooperative,
    NotSupported
}

internal sealed record CommandResourceLimits(
    int DefaultTimeoutMilliseconds,
    int MaximumItems,
    int MaximumOutputBytes);

/// <summary>
///     Host-neutral product metadata projected into discovery, schemas, help, doctor,
///     suggestions, and generated documentation.
/// </summary>
internal sealed record CommandProductMetadata(
    CapabilityMaturity Maturity,
    CommandEffects Effects,
    bool ConditionallyMutable,
    CommandIdempotency Idempotency,
    CommandAudience Audience,
    TrustRequirement TrustRequirement,
    WorkspaceRequirement WorkspaceRequirement,
    IReadOnlyList<string> Prerequisites,
    CommandCost Cost,
    CancellationSupport Cancellation,
    CommandResourceLimits Limits,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> Examples)
{
    internal static CommandProductMetadata FromLegacy(
        RtCommandInfo info,
        CommandSafety safety,
        CommandAudience audience = CommandAudience.Public,
        CommandEffects? effects = null,
        WorkspaceRequirement workspaceRequirement = WorkspaceRequirement.Semantic,
        CapabilityMaturity? maturity = null) => new(
        maturity ?? (safety.Effect switch
        {
            CommandEffect.ReadOnly => CapabilityMaturity.Preview,
            CommandEffect.LocalStateWrite or CommandEffect.Mutation => CapabilityMaturity.Experimental,
            _ => throw new ArgumentOutOfRangeException(nameof(safety))
        }),
        effects ?? (safety.Effect switch
        {
            CommandEffect.ReadOnly => CommandEffects.RepositoryRead,
            CommandEffect.LocalStateWrite => CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite,
            CommandEffect.Mutation => CommandEffects.RepositoryRead | CommandEffects.RepositoryWrite,
            _ => throw new ArgumentOutOfRangeException(nameof(safety))
        }),
        ConditionallyMutable: safety.Effect != CommandEffect.ReadOnly,
        safety.Effect switch
        {
            CommandEffect.ReadOnly => CommandIdempotency.Idempotent,
            CommandEffect.LocalStateWrite => CommandIdempotency.Conditional,
            CommandEffect.Mutation => CommandIdempotency.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(safety))
        },
        audience,
        safety.Effect == CommandEffect.LocalStateWrite
            ? TrustRequirement.None
            : TrustRequirement.ExplicitTrustedRepository,
        safety.Effect == CommandEffect.LocalStateWrite
            ? WorkspaceRequirement.None
            : workspaceRequirement,
        safety.Effect == CommandEffect.LocalStateWrite
            ? []
            : [".NET SDK", "Loadable C# solution or project"],
        CommandCost.Medium,
        CancellationSupport.Cooperative,
        new CommandResourceLimits(
            DefaultTimeoutMilliseconds: 30_000,
            MaximumItems: 100,
            MaximumOutputBytes: 1_048_576),
        info.KnownIssues ?? [],
        info.Examples);
}
