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
    public async Task trust_is_root_only_local_control_in_the_catalog_and_help()
    {
        var entry = App.Commands.Single(command => command.Info.Name == "trust");

        entry.Info.Mutates.ShouldBeFalse();
        entry.Info.JsonOutput.ShouldBeTrue();
        entry.Info.Category.ShouldBe("control");
        entry.Info.Placement.ShouldBe(CommandPlacement.RootOnly);
        App.ControlPlaneCommandNames.ShouldContain("trust");
        typeof(IControlPlaneCommand).IsAssignableFrom(typeof(TrustCommand)).ShouldBeTrue();
        typeof(IRefactorCommand).IsAssignableFrom(typeof(TrustCommand)).ShouldBeFalse();
        new AppCommandCatalog().All.ShouldContain(info => info.Name == "trust");
        DumpHelpCommand.RenderMarkdown(App.Commands.Select(command => (command.Build(), command.Info)).ToArray())
            .ShouldContain("trust", Case.Sensitive);

        var catalogue = await RtCliHost.RunAsync("dump-help --json");
        catalogue.ExitCode.ShouldBe(0, catalogue.Stderr);
        using var document = JsonDocument.Parse(catalogue.Stdout);
        var trust = document.RootElement.GetProperty("commands").EnumerateArray()
            .Single(command => command.GetProperty("name").GetString() == "trust");
        trust.GetProperty("category").GetString().ShouldBe("control");
        trust.GetProperty("group").GetString().ShouldBe("root");
        trust.GetProperty("mutates").GetBoolean().ShouldBeFalse();
        var product = trust.GetProperty("product");
        product.GetProperty("effects").EnumerateArray().Select(effect => effect.GetString())
            .ShouldBe(["RepositoryRead", "LocalStateRead", "LocalStateWrite"]);
        product.GetProperty("trustRequirement").GetString().ShouldBe("None");
        product.GetProperty("workspaceRequirement").GetString().ShouldBe("None");
        product.GetProperty("prerequisites").GetArrayLength().ShouldBe(0);
        product.GetProperty("idempotency").GetString().ShouldBe("Conditional");
        product.GetProperty("cancellation").GetString().ShouldBe("NotSupported");

        var help = await RtCliHost.RunAsync("trust --help");
        help.ExitCode.ShouldBe(0, help.Stderr);
        help.Stdout.ShouldContain("local-state-write");
        help.Stdout.ShouldNotContain("writes repository");

        var oldGroupedRoute = await RtCliHost.RunAsync("refactor trust list");
        oldGroupedRoute.ExitCode.ShouldNotBe(0);
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
            add.Definition.Safety.ShouldBe(CommandSafety.LocalStateWrite);
            remove.Definition.Safety.ShouldBe(CommandSafety.LocalStateWrite);
            list.Definition.Safety.ShouldBe(CommandSafety.ReadOnly);
            status.Definition.Safety.ShouldBe(CommandSafety.ReadOnly);
            var definitions = new[]
            {
                add.Definition, list.Definition, status.Definition, remove.Definition
            };
            definitions.ShouldAllBe(definition =>
                !definition.Info.Mutates
                && definition.Info.Category == "control"
                && definition.Info.Placement == CommandPlacement.RootOnly
                && definition.Product != null
                && definition.Product.TrustRequirement == TrustRequirement.None
                && definition.Product.WorkspaceRequirement == WorkspaceRequirement.None
                && definition.Product.Cancellation == CancellationSupport.NotSupported);
            add.Definition.Product!.Effects.ShouldBe(
                CommandEffects.RepositoryRead | CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite);
            list.Definition.Product!.Effects.ShouldBe(
                CommandEffects.RepositoryRead | CommandEffects.LocalStateRead);
            status.Definition.Product!.Effects.ShouldBe(
                CommandEffects.RepositoryRead | CommandEffects.LocalStateRead);
            remove.Definition.Product!.Effects.ShouldBe(
                CommandEffects.RepositoryRead | CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite);
            add.Definition.Product.ConditionallyMutable.ShouldBeTrue();
            remove.Definition.Product.ConditionallyMutable.ShouldBeTrue();
            list.Definition.Product.ConditionallyMutable.ShouldBeFalse();
            status.Definition.Product.ConditionallyMutable.ShouldBeFalse();
            add.Definition.Product.Idempotency.ShouldBe(CommandIdempotency.Conditional);
            list.Definition.Product.Idempotency.ShouldBe(CommandIdempotency.Idempotent);
            status.Definition.Product.Idempotency.ShouldBe(CommandIdempotency.Idempotent);
            remove.Definition.Product.Idempotency.ShouldBe(CommandIdempotency.Conditional);
            add.Definition.Product.Prerequisites.ShouldBe(["Git"]);
            list.Definition.Product.Prerequisites.ShouldBeEmpty();
            status.Definition.Product.Prerequisites.ShouldBe(["Git"]);
            remove.Definition.Product.Prerequisites.ShouldBeEmpty();
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
