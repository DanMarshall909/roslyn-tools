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
        CommandEffects.LocalStateWrite,
        CommandSafety.ReviewByDefault,
        ["rt trust add <repository>"]);

    internal static ApplicationCommandDefinition List { get; } = Create(
        "trust_list",
        "List persistent repository-trust records and their current registered-worktree continuity state.",
        typeof(TrustListRequest),
        typeof(TrustListResult),
        CommandEffects.LocalStateRead,
        CommandSafety.ReadOnly,
        ["rt trust list"]);

    internal static ApplicationCommandDefinition Status { get; } = Create(
        "trust_status",
        "Check persistent trust and preview Git-object continuity for a Git repository.",
        typeof(TrustStatusRequest),
        typeof(RepositoryTrustStatus),
        CommandEffects.LocalStateRead | CommandEffects.RepositoryRead,
        CommandSafety.ReadOnly,
        ["rt trust status <repository>"]);

    internal static ApplicationCommandDefinition Remove { get; } = Create(
        "trust_remove",
        "Remove persistent trust for a Git common-directory identity.",
        typeof(TrustRemoveRequest),
        typeof(RepositoryTrustStatus),
        CommandEffects.LocalStateWrite,
        CommandSafety.ReviewByDefault,
        ["rt trust remove <repository>"]);

    private static ApplicationCommandDefinition Create(
        string name,
        string whenToUse,
        Type requestType,
        Type responseType,
        CommandEffects effects,
        CommandSafety safety,
        string[] examples)
    {
        var info = new RtCommandInfo(
            name,
            safety.Effect == CommandEffect.Mutation,
            true,
            "trust",
            whenToUse,
            examples,
            SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);
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
                ConditionallyMutable: safety.Effect == CommandEffect.Mutation,
                CommandIdempotency.Idempotent,
                CommandAudience.Maintainer,
                TrustRequirement.None,
                WorkspaceRequirement.None,
                Prerequisites: ["Git"],
                CommandCost.Low,
                CancellationSupport.Cooperative,
                new CommandResourceLimits(5_000, 1_000, 1_048_576),
                Limitations:
                [
                    "Preview trust storage does not yet implement ADR-0011 leases, audit history, quotas, or evaluator isolation."
                ],
                examples));
    }
}
