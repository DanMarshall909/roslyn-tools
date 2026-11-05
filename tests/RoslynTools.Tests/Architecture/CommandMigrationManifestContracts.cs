using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class CommandMigrationManifestContracts
{
    private static readonly HashSet<string> Categories =
        ["inspect", "analyze", "refactor", "ci", "control"];
    private static readonly HashSet<string> WorkStates =
        ["convert", "reconcile", "complete"];
    private static readonly HashSet<string> ExecutionPlacements =
        ["daemon-preferred", "in-process"];
    private static readonly HashSet<string> SafetyPolicies =
        ["read-only", "review-default", "apply-default", "local-state-write"];
    private static readonly Type[] HostRenderingContracts =
        [typeof(IRtOutput), typeof(IRtJsonOutput), typeof(IRtMarkdownOutput), typeof(IRtYamlOutput)];

    [Fact]
    public void migration_manifest_freezes_the_complete_public_category_command_inventory()
    {
        var rows = ReadManifest();
        var commands = App.AllCommands
            .Where(command => Categories.Contains(command.Info.Category))
            .ToDictionary(command => command.Info.Name, command => command.Info, StringComparer.Ordinal);

        rows.Length.ShouldBe(67);
        rows.Select(row => row.Command).ShouldBeUnique();
        rows.Select(row => row.Command).OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(commands.Keys.OrderBy(name => name, StringComparer.Ordinal));

        foreach (var row in rows)
        {
            Categories.ShouldContain(row.Category);
            row.Group.ShouldMatch("^[RM][0-5]$");
            WorkStates.ShouldContain(row.Work);
            ExecutionPlacements.ShouldContain(row.Execution);
            SafetyPolicies.ShouldContain(row.Safety);
            commands[row.Command].Category.ShouldBe(row.Category);
        }
    }

    [Fact]
    public void every_migrated_category_definition_has_one_discovered_port_and_matches_the_manifest()
    {
        var rows = ReadManifest().ToDictionary(
            row => Normalize(row.Command),
            StringComparer.Ordinal);
        var services = new ServiceCollection();
        services.AddLogging();
        RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        var ports = provider.GetServices<ICommandApplicationPort>().ToList();
        var migratedPorts = ports
            .Where(port => port is not ILegacyCommandApplicationPort)
            .Where(port => Categories.Contains(port.Definition.Info.Category))
            .ToList();

        migratedPorts.Select(port => port.Definition.Name).ShouldBeUnique();
        migratedPorts.ShouldNotBeEmpty();

        foreach (var port in migratedPorts)
        {
            rows.TryGetValue(port.Definition.Name, out var row).ShouldBeTrue(
                $"Migrated category command '{port.Definition.Name}' is absent from the category manifest.");
            var manifestRow = row!;
            port.RequestType.ShouldBe(port.Definition.RequestType);
            port.ResponseType.ShouldBe(port.Definition.ResponseType);
            port.Definition.Info.Name.ShouldBe(manifestRow.Command);
            port.Definition.Info.Category.ShouldBe(manifestRow.Category);
            port.Definition.ExecutionPlacement.ShouldBe(ParsePlacement(manifestRow.Execution));
            AssertSafety(port.Definition.Safety, manifestRow.Safety);
            if (port.Definition.Safety.Effect == CommandEffect.Mutation)
            {
                port.Definition.McpVisible.ShouldBe(
                    port.Definition.Product.Audience == CommandAudience.Public);
                port.Definition.ExecutionPlacement.ShouldBe(CommandExecutionPlacement.InProcess);
                port.Definition.Safety.MutationDefault.ShouldBe(MutationExecutionDefault.Review);
            }
            AssertHostNeutralContract(port.RequestType);
            AssertHostNeutralContract(port.ResponseType);
        }
    }

    private static void AssertSafety(CommandSafety safety, string expected)
    {
        switch (expected)
        {
            case "read-only":
                safety.Effect.ShouldBe(CommandEffect.ReadOnly);
                safety.MutationDefault.ShouldBeNull();
                break;
            case "review-default":
                safety.Effect.ShouldBe(CommandEffect.Mutation);
                safety.MutationDefault.ShouldBe(MutationExecutionDefault.Review);
                break;
            case "apply-default":
                safety.Effect.ShouldBe(CommandEffect.Mutation);
                safety.MutationDefault.ShouldBe(MutationExecutionDefault.Apply);
                break;
            case "local-state-write":
                safety.Effect.ShouldBe(CommandEffect.LocalStateWrite);
                safety.MutationDefault.ShouldBeNull();
                break;
            default:
                throw new InvalidOperationException($"Unknown manifest safety '{expected}'.");
        }
    }

    private static CommandExecutionPlacement ParsePlacement(string placement) => placement switch
    {
        "daemon-preferred" => CommandExecutionPlacement.DaemonPreferred,
        "in-process" => CommandExecutionPlacement.InProcess,
        _ => throw new InvalidOperationException($"Unknown manifest placement '{placement}'.")
    };

    private static void AssertHostNeutralContract(Type root)
    {
        var pending = new Stack<Type>();
        var visited = new HashSet<Type>();
        pending.Push(root);

        while (pending.TryPop(out var type))
        {
            if (type.IsArray)
            {
                pending.Push(type.GetElementType()!);
                continue;
            }

            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments())
                    pending.Push(argument);

            if (!visited.Add(type))
                continue;

            var typeNamespace = type.Namespace ?? string.Empty;
            var renderingContract = HostRenderingContracts.FirstOrDefault(
                contract => contract.IsAssignableFrom(type));
            Assert.False(
                typeNamespace.StartsWith("System.CommandLine", StringComparison.Ordinal)
                || typeNamespace.StartsWith("ModelContextProtocol", StringComparison.Ordinal)
                || type.Assembly == typeof(App).Assembly
                || renderingContract is not null,
                $"Host type '{type.FullName}' leaked into contract '{root.FullName}'.");

            if (type.Assembly != typeof(ApplicationCommandDefinition).Assembly)
                continue;

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                    pending.Push(property.PropertyType);
            }

            foreach (var constructor in type.GetConstructors(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var parameter in constructor.GetParameters())
                    pending.Push(parameter.ParameterType);
        }
    }

    private static ManifestRow[] ReadManifest()
    {
        var path = FindRepositoryFile("docs", "command-port-migration-groups.tsv");
        return File.ReadLines(path)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split('\t'))
            .Select(columns =>
            {
                columns.Length.ShouldBe(6);
                return new ManifestRow(
                    columns[0],
                    columns[1],
                    columns[2],
                    columns[3],
                    columns[4],
                    columns[5]);
            })
            .ToArray();
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(parts)}'.");
    }

    private static string Normalize(string command) => command.Replace('-', '_');

    private sealed record ManifestRow(
        string Command,
        string Category,
        string Group,
        string Work,
        string Execution,
        string Safety);
}
