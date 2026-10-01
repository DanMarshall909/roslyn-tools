using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record TrustAddRequest(string Repository, bool Apply = false);
internal sealed record TrustListRequest;
internal sealed record TrustStatusRequest(string Repository);
internal sealed record TrustRemoveRequest(string Repository, bool Apply = false);

internal sealed record TrustListResult(IReadOnlyList<RepositoryTrustStatus> Repositories);

internal sealed class TrustAddOperation(RepositoryTrustRegistry registry)
    : CommandApplicationPort<TrustAddRequest, RepositoryTrustStatus>
{
    public override ApplicationCommandDefinition Definition => RepositoryTrustCommandDefinitions.Add;

    public override Task<Result<RepositoryTrustStatus, RtError>> ExecuteAsync(
        TrustAddRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(request.Apply
            ? registry.Add(request.Repository)
            : registry.Status(request.Repository));
    }
}

internal sealed class TrustListOperation(RepositoryTrustRegistry registry)
    : CommandApplicationPort<TrustListRequest, TrustListResult>
{
    public override ApplicationCommandDefinition Definition => RepositoryTrustCommandDefinitions.List;

    public override Task<Result<TrustListResult, RtError>> ExecuteAsync(
        TrustListRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(registry.List().Map(statuses => new TrustListResult(statuses)));
    }
}

internal sealed class TrustStatusOperation(RepositoryTrustRegistry registry)
    : CommandApplicationPort<TrustStatusRequest, RepositoryTrustStatus>
{
    public override ApplicationCommandDefinition Definition => RepositoryTrustCommandDefinitions.Status;

    public override Task<Result<RepositoryTrustStatus, RtError>> ExecuteAsync(
        TrustStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(registry.Status(request.Repository));
    }
}

internal sealed class TrustRemoveOperation(RepositoryTrustRegistry registry)
    : CommandApplicationPort<TrustRemoveRequest, RepositoryTrustStatus>
{
    public override ApplicationCommandDefinition Definition => RepositoryTrustCommandDefinitions.Remove;

    public override Task<Result<RepositoryTrustStatus, RtError>> ExecuteAsync(
        TrustRemoveRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(request.Apply
            ? registry.Remove(request.Repository)
            : registry.Status(request.Repository));
    }
}

internal static class RepositoryTrustCommandDefinitions
{
    internal static ApplicationCommandDefinition Add { get; } = Create(
        "trust_add",
        "Persist trust for a reviewed Git repository and all of its currently registered worktrees.",
        typeof(TrustAddRequest),
        typeof(RepositoryTrustStatus),
        CommandEffects.RepositoryRead | CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite,
        CommandSafety.LocalStateWrite,
        ["Git"],
        CommandIdempotency.Conditional,
        ["rt trust add <repository>"]);

    internal static ApplicationCommandDefinition List { get; } = Create(
        "trust_list",
        "List persistent repository-trust records and their current registered-worktree continuity state.",
        typeof(TrustListRequest),
        typeof(TrustListResult),
        CommandEffects.RepositoryRead | CommandEffects.LocalStateRead,
        CommandSafety.ReadOnly,
        [],
        CommandIdempotency.Idempotent,
        ["rt trust list"]);

    internal static ApplicationCommandDefinition Status { get; } = Create(
        "trust_status",
        "Check persistent trust and preview Git-object continuity for a Git repository.",
        typeof(TrustStatusRequest),
        typeof(RepositoryTrustStatus),
        CommandEffects.LocalStateRead | CommandEffects.RepositoryRead,
        CommandSafety.ReadOnly,
        ["Git"],
        CommandIdempotency.Idempotent,
        ["rt trust status <repository>"]);

    internal static ApplicationCommandDefinition Remove { get; } = Create(
        "trust_remove",
        "Remove persistent trust for a Git common-directory identity.",
        typeof(TrustRemoveRequest),
        typeof(RepositoryTrustStatus),
        CommandEffects.RepositoryRead | CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite,
        CommandSafety.LocalStateWrite,
        [],
        CommandIdempotency.Conditional,
        ["rt trust remove <repository>"]);

    private static ApplicationCommandDefinition Create(
        string name,
        string whenToUse,
        Type requestType,
        Type responseType,
        CommandEffects effects,
        CommandSafety safety,
        string[] prerequisites,
        CommandIdempotency idempotency,
        string[] examples)
    {
        var info = new RtCommandInfo(
            name,
            safety.Effect == CommandEffect.Mutation,
            true,
            "control",
            whenToUse,
            examples,
            SupportedFormats: [OutputFormat.Text, OutputFormat.Json],
            Placement: CommandPlacement.RootOnly);
        return new ApplicationCommandDefinition(
            info,
            requestType,
            responseType,
            mcpVisible: false,
            safety,
            CommandExecutionPlacement.InProcess,
            [],
            product: new CommandProductMetadata(
                CapabilityMaturity.Preview,
                effects,
                ConditionallyMutable: safety.Effect == CommandEffect.LocalStateWrite,
                idempotency,
                CommandAudience.Maintainer,
                TrustRequirement.None,
                WorkspaceRequirement.None,
                Prerequisites: prerequisites,
                CommandCost.Low,
                CancellationSupport.NotSupported,
                new CommandResourceLimits(30_000, 1_000, 1_048_576),
                Limitations:
                [
                    "Git is needed to validate live repository continuity; listing stored records and removal by id can continue without it.",
                    "Cancellation is checked before registry work; synchronous registry and Git operations cannot be cancelled.",
                    "CLI trust commands do not yet enforce the advertised timeout, item count, or output byte limit.",
                    "Preview trust storage does not yet implement ADR-0011 leases, audit history, quotas, or evaluator isolation."
                ],
                examples));
    }
}
