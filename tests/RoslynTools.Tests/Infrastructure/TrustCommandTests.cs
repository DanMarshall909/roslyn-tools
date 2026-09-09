using System.CommandLine;
using RoslynTools.Cli.Trust;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class TrustCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rt-trust-command-{Guid.NewGuid():N}");

    public TrustCommandTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task cli_can_add_list_and_remove_roots_without_a_solution_or_workspace()
    {
        var store = new RepositoryTrustStore(Path.Combine(_directory, "config", "trusted-roots.json"));
        var command = new RootCommand { TrustCommand.Build(new RepositoryTrustService(store)) };
        var root = Directory.CreateDirectory(Path.Combine(_directory, "repository")).FullName;
        (await command.InvokeAsync(["trust", "add", root])).ShouldBe(0);
        store.Read().ShouldBe([root]);
        (await command.InvokeAsync(["trust", "list"])).ShouldBe(0);
        Directory.Delete(root);
        (await command.InvokeAsync(["trust", "remove", root])).ShouldBe(0);
        store.Read().ShouldBeEmpty();
        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public async Task cli_rejects_relative_roots_without_writing_configuration()
    {
        var store = new RepositoryTrustStore(Path.Combine(_directory, "config", "trusted-roots.json"));
        var command = new RootCommand { TrustCommand.Build(new RepositoryTrustService(store)) };
        (await command.InvokeAsync(["trust", "add", "relative"])).ShouldNotBe(0);
        File.Exists(store.FilePath).ShouldBeFalse();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
