using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Cli.Suggest;
using RoslynTools.CommandDescriptors;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class RepositoryTrustOperationsTests
{
    [Fact]
    public void trust_is_in_the_canonical_catalog_dump_help_and_suggestions()
    {
        var entry = App.Commands.Single(command => command.Info.Name == "trust");

        entry.Info.Mutates.ShouldBeTrue();
        entry.Info.JsonOutput.ShouldBeTrue();
        entry.Info.Category.ShouldBe("refactor");
        App.ControlPlaneCommandNames.ShouldContain("trust");
        typeof(IControlPlaneCommand).IsAssignableFrom(typeof(TrustCommand)).ShouldBeTrue();
        new AppCommandCatalog().All.ShouldContain(info => info.Name == "trust");
        DumpHelpCommand.RenderMarkdown(App.Commands.Select(command => (command.Build(), command.Info)).ToArray())
            .ShouldContain("trust", Case.Sensitive);
    }

    [Fact]
    public async Task typed_ports_share_the_registry_and_remain_hidden_from_mcp()
    {
        var registryPath = Path.Combine(
            Path.GetTempPath(),
            $"rt-trust-operations-{Guid.NewGuid():N}",
            "repository-trust.json");
        try
        {
            var registry = new RepositoryTrustRegistry(registryPath);
            var add = new TrustAddOperation(registry);
            var list = new TrustListOperation(registry);
            var status = new TrustStatusOperation(registry);
            var remove = new TrustRemoveOperation(registry);
            var repositoryRoot = FindRepoRoot();

            var previewAdd = await add.ExecuteAsync(new TrustAddRequest(repositoryRoot));
            var added = await add.ExecuteAsync(new TrustAddRequest(repositoryRoot, Apply: true));
            var listed = await list.ExecuteAsync(new TrustListRequest());
            var current = await status.ExecuteAsync(new TrustStatusRequest(repositoryRoot));
            var previewRemove = await remove.ExecuteAsync(new TrustRemoveRequest(repositoryRoot));
            var removed = await remove.ExecuteAsync(new TrustRemoveRequest(repositoryRoot, Apply: true));

            previewAdd.Match(value => value.State, _ => RepositoryTrustState.Trusted)
                .ShouldBe(RepositoryTrustState.Untrusted);
            added.IsOk.ShouldBeTrue();
            listed.Match(value => value.Repositories.Count, _ => -1).ShouldBe(1);
            current.Match(value => value.State, _ => RepositoryTrustState.Untrusted)
                .ShouldBe(RepositoryTrustState.Trusted);
            previewRemove.Match(value => value.State, _ => RepositoryTrustState.Untrusted)
                .ShouldBe(RepositoryTrustState.Trusted);
            removed.Match(value => value.State, _ => RepositoryTrustState.Trusted)
                .ShouldBe(RepositoryTrustState.Untrusted);
            new ICommandApplicationPort[] { add, list, status, remove }
                .ShouldAllBe(port => !port.Definition.McpVisible);
            add.Definition.Safety.ShouldBe(CommandSafety.ReviewByDefault);
            remove.Definition.Safety.ShouldBe(CommandSafety.ReviewByDefault);
            list.Definition.Safety.ShouldBe(CommandSafety.ReadOnly);
            status.Definition.Safety.ShouldBe(CommandSafety.ReadOnly);
        }
        finally
        {
            var directory = Path.GetDirectoryName(registryPath)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void cli_group_exposes_add_list_status_and_remove()
    {
        var registry = new RepositoryTrustRegistry(Path.Combine(
            Path.GetTempPath(),
            $"rt-trust-command-{Guid.NewGuid():N}",
            "repository-trust.json"));

        var command = new TrustCommand(
            new TrustAddOperation(registry),
            new TrustListOperation(registry),
            new TrustStatusOperation(registry),
            new TrustRemoveOperation(registry)).Build();

        command.Name.ShouldBe("trust");
        command.Subcommands.Select(subcommand => subcommand.Name)
            .ShouldBe(["add", "list", "status", "remove"]);
    }

    [Fact]
    public async Task bootstrap_keeps_trust_ports_out_of_daemon_and_descriptor_enumeration()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<RepositoryTrustRegistry>().ShouldBeSameAs(
            provider.GetRequiredService<RepositoryTrustRegistry>());
        provider.GetRequiredService<TrustAddOperation>().ShouldNotBeNull();
        provider.GetRequiredService<TrustListOperation>().ShouldNotBeNull();
        provider.GetRequiredService<TrustStatusOperation>().ShouldNotBeNull();
        provider.GetRequiredService<TrustRemoveOperation>().ShouldNotBeNull();
        var names = provider.GetServices<ICommandApplicationPort>()
            .Select(port => port.Definition.Name)
            .ToArray();
        names.ShouldNotContain("trust_add");
        names.ShouldNotContain("trust_list");
        names.ShouldNotContain("trust_status");
        names.ShouldNotContain("trust_remove");
        provider.GetRequiredService<CommandDescriptorRegistry>().All
            .ShouldNotContain(descriptor => descriptor.ToolName == "trust");

        using var payload = JsonDocument.Parse("""{"repository":"."}""");
        var response = await provider.GetRequiredService<DaemonCommandDispatcher>()
            .ExecuteAsync(new DaemonRpcRequest(
                DaemonRpcProtocol.CurrentVersion,
                "forged-trust-add",
                DaemonRpcProtocol.ExecuteCommandMethod,
                "trust_add",
                payload.RootElement.Clone()));
        response.Error.ShouldNotBeNull().Code.ShouldBe(DaemonRpcErrorCodes.CommandNotFound);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }
}
