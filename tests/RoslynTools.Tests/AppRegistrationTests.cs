using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynTools.Services;

namespace RoslynTools.Tests;

/// <summary>
/// Unit tests for App.RegisterGlobalOptions — extracted helper (US-033).
/// Verifies that the five global options are registered on a RootCommand by name,
/// without needing to run the full middleware pipeline.
/// </summary>
public sealed class AppRegistrationTests
{
    [Fact]
    public void RegisterGlobalOptions_adds_json_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "json");
    }

    [Fact]
    public void RegisterGlobalOptions_adds_llm_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "llm");
    }

    [Fact]
    public void RegisterGlobalOptions_adds_markdown_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "markdown");
    }

    [Fact]
    public void RegisterGlobalOptions_adds_yaml_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "yaml");
    }

    [Fact]
    public void RegisterGlobalOptions_adds_decisions_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "decisions");
    }

    [Fact]
    public void RegisterGlobalOptions_registers_all_five_options_at_once()
    {
        var root = new RootCommand("test");
        var before = root.Options.Count;

        App.RegisterGlobalOptions(root);

        Assert.Equal(before + 5, root.Options.Count);
    }

    [Fact]
    public void Bootstrap_registers_services_by_marker_interface()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Bootstrap.RegisterServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<MigrateToIOptionsService>());
        Assert.Contains(provider.GetServices<IRtService>(), service => service is MigrateToIOptionsService);
    }

    [Fact]
    public void Every_concrete_service_implements_the_marker_interface()
    {
        var missing = typeof(MigrateToIOptionsService).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                           && type.Namespace == "RoslynTools.Services"
                           && type.Name.EndsWith("Service", StringComparison.Ordinal)
                           && !typeof(IRtService).IsAssignableFrom(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }
}
