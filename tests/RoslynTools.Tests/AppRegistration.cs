using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Services;

namespace RoslynTools.Tests;

/// <summary>
///     Unit tests for App.RegisterGlobalOptions — extracted helper (US-033).
///     Verifies that the global options are registered on a RootCommand by name,
///     without needing to run the full middleware pipeline.
/// </summary>
public sealed class AppRegistration
{
    [Fact]
    public void registers_global_options_adds_json_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "json");
    }

    [Fact]
    public void registers_global_options_adds_llm_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "llm");
    }

    [Fact]
    public void registers_global_options_adds_markdown_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "markdown");
    }

    [Fact]
    public void registers_global_options_adds_yaml_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "yaml");
    }

    [Fact]
    public void registers_global_options_adds_decisions_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "decisions");
    }

    [Fact]
    public void registers_global_options_adds_strict_json_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "strict-json");
    }

    [Fact]
    public void registers_global_options_adds_show_workspace_warnings_option()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        Assert.Contains(root.Options, o => o.Name == "show-workspace-warnings");
    }

    [Fact]
    public void registers_global_options_registers_exactly_expected_options()
    {
        var root = new RootCommand("test");

        App.RegisterGlobalOptions(root);

        var names = root.Options.Select(o => o.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "json",
                "llm",
                "markdown",
                "yaml",
                "strict-json",
                "decisions",
                "show-workspace-warnings"
            },
            names);
    }

    [Fact]
    public void registers_services_by_marker_interface()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        RoslynTools.Bootstrap.RegisterServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<MigrateToIOptionsService>());
        Assert.Contains(provider.GetServices<IRtService>(), service => service is MigrateToIOptionsService);
    }

    [Fact]
    public void resolves_every_concrete_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        RoslynTools.Bootstrap.RegisterServices(services);

        using var provider = services.BuildServiceProvider();
        var serviceTypes = GetConcreteServiceTypes();

        var unresolved = serviceTypes
            .Where(type => provider.GetService(type) is null)
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void every_concrete_service_implements_the_marker_interface()
    {
        var missing = GetConcreteServiceTypes()
            .Where(type => !typeof(IRtService).IsAssignableFrom(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void writes_solution_changes_through_the_injected_writer()
    {
        var repoRoot = FindRepoRoot();
        var serviceFiles = Directory.EnumerateFiles(
            Path.Combine(repoRoot, "src", "RoslynTools", "Services"),
            "*.cs",
            SearchOption.TopDirectoryOnly);

        var directUsages = serviceFiles
            .Where(path => !Path.GetFileName(path).Equals("SolutionChangeWriter.cs", StringComparison.Ordinal))
            .Where(path =>
                File.ReadAllText(path).Contains("SolutionWriter.ApplyChangesAsync", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(directUsages);
    }

    [Fact]
    public void resolves_symbols_through_injected_services()
    {
        var repoRoot = FindRepoRoot();
        var serviceFiles = Directory.EnumerateFiles(
            Path.Combine(repoRoot, "src", "RoslynTools", "Services"),
            "*.cs",
            SearchOption.TopDirectoryOnly);

        var directUsages = serviceFiles
            .Where(path => !Path.GetFileName(path).Equals("SymbolResolutionService.cs", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).Equals("DocumentLocatorService.cs", StringComparison.Ordinal))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("SymbolLocator.", StringComparison.Ordinal)
                       || text.Contains("SymbolDocumentLocator.", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(directUsages);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static Type[] GetConcreteServiceTypes()
    {
        return typeof(MigrateToIOptionsService).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                           && type.Namespace == "RoslynTools.Services"
                           && type.Name.EndsWith("Service", StringComparison.Ordinal))
            .ToArray();
    }
}
