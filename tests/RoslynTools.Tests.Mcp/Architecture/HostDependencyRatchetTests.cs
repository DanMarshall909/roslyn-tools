using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Configuration;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Daemon;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Architecture;

public sealed class HostDependencyRatchetTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["RoslynTools"] = [],
            ["RoslynTools.Hosting"] = ["RoslynTools"],
            ["RoslynTools.DaemonHost"] = ["RoslynTools", "RoslynTools.Hosting"],
            ["RoslynTools.Mcp"] = ["RoslynTools", "RoslynTools.Hosting"],
            ["RoslynTools.Cli"] =
                ["RoslynTools", "RoslynTools.Hosting", "RoslynTools.DaemonHost"]
        };

    [Fact]
    public void production_projects_follow_the_declared_core_and_peer_host_graph()
    {
        var root = FindRepositoryRoot();
        var solution = XDocument.Load(Path.Combine(root, "RoslynTools.slnx"));
        var productionProjects = solution.Descendants("Project")
            .Select(project => project.Attribute("Path")?.Value)
            .Where(path => path is not null && path.StartsWith("src/", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .Order(StringComparer.Ordinal)
            .ToArray();
        productionProjects.ShouldBe(ProjectReferences.Keys.Order(StringComparer.Ordinal));

        foreach (var (projectName, expectedReferences) in ProjectReferences)
        {
            var project = XDocument.Load(Path.Combine(root, "src", projectName, projectName + ".csproj"));
            var actualReferences = project.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Select(path => Path.GetFileNameWithoutExtension(path!.Replace('\\', '/')))
                .Order(StringComparer.Ordinal)
                .ToArray();
            actualReferences.ShouldBe(expectedReferences.Order(StringComparer.Ordinal));

            if (projectName == "RoslynTools.Cli")
                continue;

            var packages = project.Descendants("PackageReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .ToArray();
            packages.ShouldNotContain(package => IsFamily(package, "System.CommandLine"));
            packages.ShouldNotContain(package => IsFamily(package, "Spectre.Console"));
            if (projectName is "RoslynTools" or "RoslynTools.Hosting" or "RoslynTools.DaemonHost")
                packages.ShouldNotContain(package => IsFamily(package, "ModelContextProtocol"));
            if (projectName == "RoslynTools")
                packages.ShouldNotContain(package => IsFamily(package, "Microsoft.Extensions.Hosting"));
        }
    }

    [Fact]
    public void compiled_core_and_host_assemblies_do_not_reference_forbidden_peers()
    {
        AssertNoReferences(typeof(ApplicationCommandCatalog).Assembly,
            "RoslynTools.Hosting", "RoslynTools.DaemonHost", "RoslynTools.Mcp", "rt",
            "System.CommandLine", "Spectre.Console", "ModelContextProtocol",
            "Microsoft.Extensions.Hosting");
        AssertNoReferences(typeof(HostServiceRegistration).Assembly,
            "RoslynTools.DaemonHost", "RoslynTools.Mcp", "rt",
            "System.CommandLine", "Spectre.Console", "ModelContextProtocol");
        AssertNoReferences(typeof(DaemonServer).Assembly,
            "RoslynTools.Mcp", "rt", "System.CommandLine", "Spectre.Console",
            "ModelContextProtocol");
        AssertNoReferences(typeof(McpServiceRegistration).Assembly,
            "RoslynTools.DaemonHost", "rt", "System.CommandLine", "Spectre.Console");
        AssertNoReferences(typeof(App).Assembly, "RoslynTools.Mcp", "ModelContextProtocol");
    }

    [Fact]
    public void cli_mcp_and_daemon_compose_the_same_typed_application_contracts()
    {
        var cliServices = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(cliServices);
        using var cliProvider = cliServices.BuildServiceProvider();

        using var mcpProvider = new ServiceCollection()
            .AddMcpHostServices(TextReader.Null, TextWriter.Null)
            .BuildServiceProvider();

        var daemonServices = new ServiceCollection();
        var identity = MsBuildInstanceIdentity.Create(
            Path.Combine(Path.GetTempPath(), "daemon-host-sdk"),
            new Version(10, 0, 100));
        DaemonHostServiceRegistration.RegisterServices(daemonServices, identity);
        using var daemonProvider = daemonServices.BuildServiceProvider();

        var expected = Contracts(mcpProvider);
        expected.ShouldNotBeEmpty();
        Contracts(cliProvider).ShouldBe(expected);
        Contracts(daemonProvider).ShouldBe(expected);
    }

    [Fact]
    public void daemon_rpc_transport_and_dispatch_live_in_shared_hosting()
    {
        var hosting = typeof(HostServiceRegistration).Assembly;
        typeof(DaemonRpcClient).Assembly.ShouldBe(hosting);
        typeof(DaemonRpcRecoveryCoordinator).Assembly.ShouldBe(hosting);
        typeof(DaemonCommandDispatcher).Assembly.ShouldBe(hosting);
        typeof(DaemonLauncher).Assembly.ShouldBe(hosting);
        typeof(DaemonRpcRequest).Assembly.ShouldBe(hosting);
        typeof(DaemonRuntime).Assembly.ShouldBe(hosting);
        typeof(DaemonOwnedCommands).Assembly.ShouldBe(hosting);
        typeof(DaemonCommandDefinition).Assembly.ShouldBe(hosting);

        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "RoslynTools");
        var namedPipeSources = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(sourceRoot, path)
                .Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .Where(path => Regex.IsMatch(
                File.ReadAllText(path),
                @"\b(?:System\.IO\.Pipes|NamedPipe(?:Client|Server)Stream)\b",
                RegexOptions.CultureInvariant))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        namedPipeSources.ShouldBeEmpty();
    }

    [Fact]
    public void process_host_configuration_and_mcp_policy_live_in_shared_hosting()
    {
        var hosting = typeof(HostServiceRegistration).Assembly;
        typeof(HostConfigurationLoader).Assembly.ShouldBe(hosting);
        typeof(HostConfigurationResolution).Assembly.ShouldBe(hosting);
        typeof(EffectiveDaemonConfiguration).Assembly.ShouldBe(hosting);
        typeof(EffectiveMcpConfiguration).Assembly.ShouldBe(hosting);
        typeof(McpConfigurationValidator).Assembly.ShouldBe(hosting);

        var core = Path.Combine(FindRepositoryRoot(), "src", "RoslynTools", "Core");
        File.Exists(Path.Combine(core, "HostConfiguration.cs")).ShouldBeFalse();
        File.Exists(Path.Combine(core, "McpConfigurationValidator.cs")).ShouldBeFalse();
    }

    private static (string Name, string Request, string Response, CommandSafetyPolicy Safety, bool McpVisible)[]
        Contracts(IServiceProvider provider) =>
        provider.GetRequiredService<ApplicationCommandCatalog>().All
            .Select(descriptor => (
                descriptor.ToolName,
                descriptor.Definition!.RequestType.FullName!,
                descriptor.Definition.ResponseType.FullName!,
                descriptor.Safety,
                descriptor.McpVisible))
            .OrderBy(contract => contract.ToolName, StringComparer.Ordinal)
            .ToArray();

    private static void AssertNoReferences(System.Reflection.Assembly assembly, params string[] forbidden)
    {
        var references = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        foreach (var name in forbidden)
            references.ShouldNotContain(reference => IsFamily(reference, name));
    }

    private static bool IsFamily(string? candidate, string name) =>
        string.Equals(candidate, name, StringComparison.Ordinal)
        || (candidate?.StartsWith(name + ".", StringComparison.Ordinal) ?? false);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
