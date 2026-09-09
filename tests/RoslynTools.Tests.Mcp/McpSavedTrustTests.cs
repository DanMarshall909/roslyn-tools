using RoslynTools.Core;
using RoslynTools.Mcp;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

public sealed class McpSavedTrustTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rt-mcp-saved-trust-{Guid.NewGuid():N}");

    public McpSavedTrustTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void startup_can_combine_multiple_roots_and_saved_trust_opt_in()
    {
        McpHostOptions.TryParse(["--use-trusted-roots", "--trust-repository", _directory,
                "--use-trusted-roots", "--trust-repository", _directory],
            out var options, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        options.UseTrustedRoots.ShouldBeTrue();
        options.TrustedRepositoryRoots.ShouldBe([_directory]);
    }

    [Fact]
    public void each_request_reloads_saved_roots_and_preserves_separate_startup_grants()
    {
        var startupRoot = Directory.CreateDirectory(Path.Combine(_directory, "startup")).FullName;
        var savedRoot = Directory.CreateDirectory(Path.Combine(_directory, "saved")).FullName;
        var outsideRoot = Directory.CreateDirectory(Path.Combine(_directory, "saved-sibling")).FullName;
        var store = new RepositoryTrustStore(Path.Combine(_directory, "trusted-roots.json"));
        var options = new McpHostOptions(startupRoot, useTrustedRoots: true);

        using (options.EnterTrustScope(store))
            RepositoryTrustContext.IsExplicitlyTrustedFor(savedRoot).ShouldBeFalse();
        store.Add(savedRoot);
        using (options.EnterTrustScope(store))
        {
            RepositoryTrustContext.IsExplicitlyTrustedFor(savedRoot).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(startupRoot).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(outsideRoot).ShouldBeFalse();
        }
        store.Remove(savedRoot);
        using (options.EnterTrustScope(store))
        {
            RepositoryTrustContext.IsExplicitlyTrustedFor(savedRoot).ShouldBeFalse();
            RepositoryTrustContext.IsExplicitlyTrustedFor(startupRoot).ShouldBeTrue();
        }
        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public void saved_registry_is_not_read_without_opt_in_or_for_administrative_scope()
    {
        var path = Path.Combine(_directory, "trusted-roots.json");
        File.WriteAllText(path, "invalid JSON");
        var store = new RepositoryTrustStore(path);

        using (new McpHostOptions().EnterTrustScope(store))
            RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
        using (new McpHostOptions(useTrustedRoots: true).EnterTrustScope(store, loadSavedRoots: false))
            RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
        Should.Throw<InvalidDataException>(() => new McpHostOptions(useTrustedRoots: true).EnterTrustScope(store));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
