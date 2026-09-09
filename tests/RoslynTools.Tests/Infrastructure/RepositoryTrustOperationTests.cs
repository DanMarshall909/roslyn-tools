using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class RepositoryTrustOperationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rt-trust-ports-{Guid.NewGuid():N}");
    private readonly RepositoryTrustStore _store;
    private readonly RepositoryTrustService _service;

    public RepositoryTrustOperationTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new RepositoryTrustStore(Path.Combine(_directory, "config", "trusted-roots.json"));
        _service = new RepositoryTrustService(_store);
    }

    [Fact]
    public async Task default_add_preview_neither_requests_approval_nor_writes_configuration()
    {
        var approval = new RecordingApproval(true);
        var operation = new TrustAddOperation(_service, approval);
        var result = await operation.ExecuteAsync(new TrustAddRequest(_directory));
        result.Unwrap().DryRun.ShouldBeTrue();
        result.Unwrap().Changed.ShouldBeFalse();
        approval.Roots.ShouldBeEmpty();
        File.Exists(_store.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task explicit_apply_cannot_grant_trust_without_host_approval()
    {
        ICommandApplicationPort operation = new TrustAddOperation(_service, new DenyRepositoryTrustApproval());
        var result = await operation.InvokeAsync(new TrustAddRequest(_directory, DryRun: false));
        result.IsError.ShouldBeTrue();
        result.Error.ShouldNotBeNull().Kind.ShouldBe(RtErrorKind.TrustRequired);
        File.Exists(_store.FilePath).ShouldBeFalse();
        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public async Task host_approval_receives_exact_canonical_root_before_persisting()
    {
        var approval = new RecordingApproval(true);
        var operation = new TrustAddOperation(_service, approval);
        var result = await operation.ExecuteAsync(new TrustAddRequest(Path.Combine(_directory, "."), DryRun: false));
        result.Unwrap().Changed.ShouldBeTrue();
        approval.Roots.ShouldBe([_directory]);
        _store.Read().ShouldBe([_directory]);
        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public async Task invalid_root_fails_before_requesting_approval()
    {
        var approval = new RecordingApproval(true);
        var operation = new TrustAddOperation(_service, approval);
        var result = await operation.ExecuteAsync(new TrustAddRequest("relative", DryRun: false));
        result.IsErr.ShouldBeTrue();
        approval.Roots.ShouldBeEmpty();
        File.Exists(_store.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task corrupt_registry_fails_before_requesting_approval()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllText(_store.FilePath, "broken");
        var approval = new RecordingApproval(true);
        var operation = new TrustAddOperation(_service, approval);
        var result = await operation.ExecuteAsync(new TrustAddRequest(_directory, DryRun: false));
        result.IsErr.ShouldBeTrue();
        approval.Roots.ShouldBeEmpty();
        File.ReadAllText(_store.FilePath).ShouldBe("broken");
    }

    [Fact]
    public async Task removal_previews_then_revokes_through_the_shared_port()
    {
        _store.Add(_directory);
        var operation = new TrustRemoveOperation(_service);
        (await operation.ExecuteAsync(new TrustRemoveRequest(_directory))).Unwrap().DryRun.ShouldBeTrue();
        _store.Read().ShouldBe([_directory]);
        (await operation.ExecuteAsync(new TrustRemoveRequest(_directory, DryRun: false))).Unwrap().Changed.ShouldBeTrue();
        (await new TrustListOperation(_service).ExecuteAsync(new TrustListRequest())).Unwrap().Roots.ShouldBeEmpty();
        Directory.Exists(_directory).ShouldBeTrue();
    }

    [Fact]
    public async Task cancellation_during_approval_cannot_persist_a_root()
    {
        using var cancellation = new CancellationTokenSource();
        var approval = new CancellingApproval(cancellation);
        var operation = new TrustAddOperation(_service, approval);
        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new TrustAddRequest(_directory, DryRun: false), cancellation.Token));
        File.Exists(_store.FilePath).ShouldBeFalse();
    }

    [Fact]
    public void definitions_require_no_workspace_or_ambient_trust_and_execute_in_process()
    {
        foreach (var definition in new[]
                 {
                     RepositoryTrustCommandDefinitions.Add, RepositoryTrustCommandDefinitions.List,
                     RepositoryTrustCommandDefinitions.Remove
                 })
        {
            definition.McpVisible.ShouldBeTrue();
            definition.ExecutionPlacement.ShouldBe(CommandExecutionPlacement.InProcess);
            definition.Product.WorkspaceRequirement.ShouldBe(WorkspaceRequirement.None);
            definition.Product.TrustRequirement.ShouldBe(TrustRequirement.None);
            definition.Product.Effects.HasFlag(CommandEffects.RepositoryCodeExecution).ShouldBeFalse();
        }
        RepositoryTrustCommandDefinitions.Add.Safety.ShouldBe(CommandSafety.ReviewByDefault);
        RepositoryTrustCommandDefinitions.Remove.Safety.ShouldBe(CommandSafety.ReviewByDefault);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class RecordingApproval(bool accepted) : IRepositoryTrustApproval
    {
        internal List<string> Roots { get; } = [];

        public Task<bool> ApproveAsync(string canonicalRoot, CancellationToken cancellationToken = default)
        {
            Roots.Add(canonicalRoot);
            return Task.FromResult(accepted);
        }
    }

    private sealed class CancellingApproval(CancellationTokenSource cancellation) : IRepositoryTrustApproval
    {
        public Task<bool> ApproveAsync(string canonicalRoot, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return Task.FromResult(true);
        }
    }
}
