using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

/// <summary>Host-supplied confirmation boundary. Request arguments cannot implement or bypass this approval.</summary>
internal interface IRepositoryTrustApproval
{
    Task<bool> ApproveAsync(string canonicalRoot, CancellationToken cancellationToken = default);
}

internal sealed class DenyRepositoryTrustApproval : IRepositoryTrustApproval
{
    public Task<bool> ApproveAsync(string canonicalRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}

internal sealed record TrustListRequest;

internal sealed record TrustAddRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Absolute existing root to approve for build evaluation; linked worktrees need separate approval")]
    string Root,
    [property: JsonPropertyName("dry-run")]
    [property: CommandPropertyDescription("Preview without changing saved trust (default). Applying still requires host-mediated user approval")]
    bool DryRun = true);

internal sealed record TrustRemoveRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Exact absolute saved root to revoke, even if the checkout no longer exists")]
    string Root,
    [property: JsonPropertyName("dry-run")]
    [property: CommandPropertyDescription("Preview without revoking saved trust (default)")]
    bool DryRun = true);

internal sealed record RepositoryTrustPlan(string RegistryPath, string Root, string Action, bool DryRun, bool Changed);

internal sealed class TrustListOperation(RepositoryTrustService service)
    : CommandApplicationPort<TrustListRequest, RepositoryTrustListing>, IRtService
{
    public override ApplicationCommandDefinition Definition => RepositoryTrustCommandDefinitions.List;

    public override Task<Result<RepositoryTrustListing, RtError>> ExecuteAsync(
        TrustListRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(service.List());
    }
}

internal sealed class TrustAddOperation(RepositoryTrustService service, IRepositoryTrustApproval approval)
    : CommandApplicationPort<TrustAddRequest, RepositoryTrustPlan>, IRtService
{
    public override ApplicationCommandDefinition Definition => RepositoryTrustCommandDefinitions.Add;

    public override async Task<Result<RepositoryTrustPlan, RtError>> ExecuteAsync(
        TrustAddRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = RepositoryTrustService.ValidateRoot(request.Root, requireExists: true);
        if (root.IsErr)
            return root.Match(_ => throw new InvalidOperationException(), Result<RepositoryTrustPlan, RtError>.Err);
        var canonicalRoot = root.Unwrap();
        var listing = service.List();
        if (listing.IsErr)
            return listing.Match(_ => throw new InvalidOperationException(), Result<RepositoryTrustPlan, RtError>.Err);
        if (request.DryRun)
            return Result<RepositoryTrustPlan, RtError>.Ok(new RepositoryTrustPlan(
                listing.Unwrap().RegistryPath, canonicalRoot, "add", DryRun: true, Changed: false));
        if (!await approval.ApproveAsync(canonicalRoot, cancellationToken))
            return Result<RepositoryTrustPlan, RtError>.Err(new RtError(RtErrorKind.TrustRequired,
                "The host did not confirm user approval for this root. Use an approval-capable MCP client or run rt trust add <root> explicitly."));
        cancellationToken.ThrowIfCancellationRequested();
        return service.Add(canonicalRoot, cancellationToken).Map(change => new RepositoryTrustPlan(
            change.RegistryPath, change.Root, "add", DryRun: false, change.Changed));
    }
}

internal sealed class TrustRemoveOperation(RepositoryTrustService service)
    : CommandApplicationPort<TrustRemoveRequest, RepositoryTrustPlan>, IRtService
{
    public override ApplicationCommandDefinition Definition => RepositoryTrustCommandDefinitions.Remove;

    public override Task<Result<RepositoryTrustPlan, RtError>> ExecuteAsync(
        TrustRemoveRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = RepositoryTrustService.ValidateRoot(request.Root, requireExists: false);
        if (root.IsErr)
            return Task.FromResult(root.Match(_ => throw new InvalidOperationException(), Result<RepositoryTrustPlan, RtError>.Err));
        var canonicalRoot = root.Unwrap();
        if (!request.DryRun)
            return Task.FromResult(service.Remove(canonicalRoot, cancellationToken).Map(change => new RepositoryTrustPlan(
                change.RegistryPath, change.Root, "remove", DryRun: false, change.Changed)));
        return Task.FromResult(service.List().Map(listing => new RepositoryTrustPlan(
            listing.RegistryPath, canonicalRoot, "remove", DryRun: true, Changed: false)));
    }
}

internal static class RepositoryTrustCommandDefinitions
{
    internal static ApplicationCommandDefinition List { get; } = Create(
        "list-trusted-roots", typeof(TrustListRequest), typeof(RepositoryTrustListing), false,
        "Use this instead of Read when checking which roots have user-owned saved approval and where the configuration lives, without evaluating a repository.",
        "rt trust list");

    internal static ApplicationCommandDefinition Add { get; } = Create(
        "trust-root", typeof(TrustAddRequest), typeof(RepositoryTrustPlan), true,
        "Use this instead of Edit when an additional repository root needs saved approval. Preview is the default; applying requires separate host-mediated user confirmation because build evaluation can execute repository code.",
        "rt trust add C:\\code\\app");

    internal static ApplicationCommandDefinition Remove { get; } = Create(
        "untrust-root", typeof(TrustRemoveRequest), typeof(RepositoryTrustPlan), true,
        "Use this instead of Edit when a saved root approval should be revoked without deleting repository files. Preview is the default; set dry-run to false to revoke the exact root.",
        "rt trust remove C:\\code\\app");

    private static ApplicationCommandDefinition Create(string name, Type requestType, Type responseType,
        bool mutates, string whenToUse, string example)
    {
        var info = new RtCommandInfo(name, mutates, true, "trust", whenToUse, [example],
            SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);
        var safety = mutates ? CommandSafety.ReviewByDefault : CommandSafety.ReadOnly;
        var effects = CommandEffects.LocalStateRead | (mutates ? CommandEffects.LocalStateWrite : CommandEffects.None);
        return new ApplicationCommandDefinition(info, requestType, responseType, mcpVisible: true,
            safety, CommandExecutionPlacement.InProcess, [], product: CommandProductMetadata.FromLegacy(
                info, safety, effects: effects, workspaceRequirement: WorkspaceRequirement.None,
                maturity: CapabilityMaturity.Preview) with
            {
                TrustRequirement = TrustRequirement.None,
                Prerequisites = [],
                Cost = CommandCost.Low,
                Idempotency = CommandIdempotency.Idempotent
            });
    }
}
