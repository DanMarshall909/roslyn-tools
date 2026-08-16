namespace RoslynTools.Core;

/// <summary>
///     Transport-neutral safety metadata for a command operation. Adapters project this
///     policy into their native invocation surface; they do not define their own policy.
/// </summary>
internal sealed record CommandSafety
{
    public CommandSafety(
        CommandEffect effect,
        MutationExecutionDefault? mutationDefault,
        bool supportsDryRun)
    {
        if (effect == CommandEffect.ReadOnly && (mutationDefault is not null || supportsDryRun))
            throw new ArgumentException("A read-only command cannot declare mutation behavior.");

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
}

internal enum CommandEffect
{
    ReadOnly,
    Mutation
}

internal enum MutationExecutionDefault
{
    Apply,
    Review
}
