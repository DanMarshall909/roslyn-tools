namespace RoslynTools.Core;

/// <summary>
///     Transport-neutral safety metadata for a command operation. Adapters project this
///     policy into their native invocation surface; they do not define their own policy.
/// </summary>
internal sealed record CommandSafety
{
    internal CommandSafety(
        CommandEffect effect,
        MutationExecutionDefault? mutationDefault,
        bool supportsDryRun)
    {
        if (effect != CommandEffect.Mutation && (mutationDefault is not null || supportsDryRun))
            throw new ArgumentException("A non-repository-write command cannot declare mutation behavior.");

        if (effect == CommandEffect.Mutation && (mutationDefault is null || !supportsDryRun))
            throw new ArgumentException("A mutation must declare its default behavior and support dry-run.");

        Effect = effect;
        MutationDefault = mutationDefault;
        SupportsDryRun = supportsDryRun;
    }

    public CommandEffect Effect { get; }

    public MutationExecutionDefault? MutationDefault { get; }

    public bool SupportsDryRun { get; }

    internal static CommandSafety ReadOnly { get; } = new(
        CommandEffect.ReadOnly,
        mutationDefault: null,
        supportsDryRun: false);

    /// <summary>
    ///     A command that writes protected host-local control state without writing the
    ///     repository. It is never projected as read-only and has no fake dry-run mode.
    /// </summary>
    internal static CommandSafety LocalStateWrite { get; } = new(
        CommandEffect.LocalStateWrite,
        mutationDefault: null,
        supportsDryRun: false);

    /// <summary>A mutation that applies unless the caller explicitly requests a preview.</summary>
    internal static CommandSafety Mutation { get; } = new(
        CommandEffect.Mutation,
        MutationExecutionDefault.Apply,
        supportsDryRun: true);

    /// <summary>A mutation that previews unless the caller explicitly requests application.</summary>
    internal static CommandSafety ReviewByDefault { get; } = new(
        CommandEffect.Mutation,
        MutationExecutionDefault.Review,
        supportsDryRun: true);

    /// <summary>
    ///     Resolves the safety label used by public discovery. Typed product effects are
    ///     authoritative when present; legacy metadata remains the compatibility fallback.
    /// </summary>
    internal static CommandSafety ForPublication(
        RtCommandInfo info,
        CommandProductMetadata? product = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (product?.Effects.HasFlag(CommandEffects.RepositoryWrite) == true)
            return Mutation;
        if (product?.Effects.HasFlag(CommandEffects.LocalStateWrite) == true)
            return LocalStateWrite;
        return info.Mutates ? Mutation : ReadOnly;
    }
}

internal enum CommandEffect
{
    ReadOnly,
    LocalStateWrite,
    Mutation
}

internal enum MutationExecutionDefault
{
    Apply,
    Review
}
