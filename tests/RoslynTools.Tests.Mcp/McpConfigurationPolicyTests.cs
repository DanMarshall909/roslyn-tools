using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Configuration;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

public sealed class McpConfigurationPolicyTests
{
    [Fact]
    public async Task excluded_tools_are_absent_from_discovery_and_rejected_by_dispatch()
    {
        using var fixture = ConfigurationFixture.Create(
            """
            {
              "schemaVersion": 1,
              "mcp": { "enabledTools": ["outline"], "disabledTools": ["source"] }
            }
            """);
        var options = new McpHostOptions(configuration: fixture.Resolution);
        using var provider = McpTestHost.CreateProvider(options);
        var registry = provider.GetRequiredService<ToolRegistry>();

        registry.GetTools().Select(tool => tool.name).ShouldBe(["outline"]);

        var rejected = await registry.InvokeAsync(
            "source",
            JsonSerializer.SerializeToElement(new { symbol = "Example" }));
        rejected.isError.ShouldBeTrue();
        rejected.content.ShouldBe("Unknown tool: source");
    }

    [Fact]
    public void unknown_exact_tool_name_is_a_file_specific_startup_error()
    {
        using var fixture = ConfigurationFixture.Create(
            """
            {
              "schemaVersion": 1,
              "mcp": { "enabledTools": ["missing_tool"] }
            }
            """);
        var options = new McpHostOptions(configuration: fixture.Resolution);
        using var provider = McpTestHost.CreateProvider(options);
        var descriptors = provider.GetRequiredService<CommandDescriptorRegistry>();

        var validated = McpConfigurationValidator.Validate(fixture.Resolution, descriptors);

        validated.IsValid.ShouldBeFalse();
        validated.RepositoryFile.IsValid.ShouldBeFalse();
        validated.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Path == fixture.ConfigurationPath
            && diagnostic.Message.Contains("missing_tool", StringComparison.Ordinal));
    }

    [Fact]
    public void repository_policy_cannot_grant_trust_for_a_write_tool()
    {
        using var fixture = ConfigurationFixture.Create(
            """{"schemaVersion":1,"mcp":{"enabledTools":["move_slice"]}}""");
        using var untrusted = McpTestHost.CreateProvider(
            new McpHostOptions(configuration: fixture.Resolution));
        using var trusted = McpTestHost.CreateProvider(
            new McpHostOptions(fixture.Repository, configuration: fixture.Resolution));

        untrusted.GetRequiredService<ToolRegistry>().GetTools().ShouldBeEmpty();
        trusted.GetRequiredService<ToolRegistry>().GetTools()
            .Select(tool => tool.name).ShouldBe(["move_slice"]);
    }

    [Fact]
    public void mcp_policy_does_not_remove_the_corresponding_cli_command()
    {
        using var fixture = ConfigurationFixture.Create(
            """{"schemaVersion":1,"mcp":{"disabledTools":["outline"]}}""");
        using var provider = McpTestHost.CreateProvider(
            new McpHostOptions(configuration: fixture.Resolution));

        provider.GetRequiredService<ToolRegistry>().GetTools()
            .Select(tool => tool.name).ShouldNotContain("outline");
        global::RoslynTools.App.Commands.Select(command => command.Info.Name)
            .ShouldContain("outline");
    }

    [Fact]
    public void configuration_inspection_remains_local_to_the_cli_host()
    {
        using var fixture = ConfigurationFixture.Create("""{"schemaVersion":1}""");
        using var provider = McpTestHost.CreateProvider(
            new McpHostOptions(configuration: fixture.Resolution));

        global::RoslynTools.App.LocalOnlyCommandNames.ShouldContain("config");
        global::RoslynTools.App.Commands.Select(command => command.Info.Name)
            .ShouldContain("config");
        provider.GetRequiredService<CommandDescriptorRegistry>().All
            .ShouldNotContain(descriptor => descriptor.ToolName == "config");
    }

    [Fact]
    public async Task mcp_process_rejects_an_unknown_configured_tool_before_protocol_input()
    {
        using var fixture = ConfigurationFixture.Create(
            """{"schemaVersion":1,"mcp":{"enabledTools":["missing_tool"]}}""");

        var result = await RunMcpAsync(fixture, input: null);

        result.ExitCode.ShouldBe(2);
        result.Stdout.ShouldBeEmpty();
        result.Stderr.ShouldContain("missing_tool");
        result.Stderr.ShouldContain(fixture.ConfigurationPath);
    }

    [Fact]
    public async Task mcp_process_applies_repository_policy_to_tools_list()
    {
        using var fixture = ConfigurationFixture.Create(
            """{"schemaVersion":1,"mcp":{"enabledTools":["outline"]}}""");

        var result = await RunMcpAsync(
            fixture,
            """{"jsonrpc":"2.0","method":"tools/list","id":1}""");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var document = JsonDocument.Parse(result.Stdout);
        document.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(tool => tool.GetProperty("name").GetString())
            .ShouldBe(["outline"]);
    }

    private static async Task<ProcessResult> RunMcpAsync(
        ConfigurationFixture fixture,
        string? input)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = fixture.Repository,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(FindMcpDll());
        startInfo.Environment["HOME"] = fixture.Home;
        startInfo.Environment["USERPROFILE"] = fixture.Home;
        startInfo.Environment["XDG_DATA_HOME"] = Path.Combine(fixture.Home, ".local", "share");
        startInfo.Environment["LOCALAPPDATA"] = Path.Combine(fixture.Home, ".local", "share");
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Failed to start the MCP host.");
        if (input is not null)
            await process.StandardInput.WriteLineAsync(input);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static string FindMcpDll()
    {
        var testBin = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = testBin.Name;
        var configuration = testBin.Parent?.Name
                            ?? throw new InvalidOperationException("Could not resolve test configuration.");
        for (var directory = testBin; directory is not null; directory = directory.Parent)
        {
            var solution = Path.Combine(directory.FullName, "RoslynTools.slnx");
            if (File.Exists(solution))
                return Path.Combine(
                    directory.FullName,
                    "src",
                    "RoslynTools.Mcp",
                    "bin",
                    configuration,
                    targetFramework,
                    "RoslynTools.Mcp.dll");
        }

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private sealed class ConfigurationFixture : IDisposable
    {
        private ConfigurationFixture(
            string root,
            string configurationPath,
            HostConfigurationResolution resolution)
        {
            Root = root;
            Home = Path.Combine(root, "home");
            Repository = Path.Combine(root, "repo");
            ConfigurationPath = configurationPath;
            Resolution = resolution;
        }

        private string Root { get; }
        internal string Home { get; }
        internal string Repository { get; }
        internal string ConfigurationPath { get; }
        internal HostConfigurationResolution Resolution { get; }

        internal static ConfigurationFixture Create(string json)
        {
            var root = Path.Combine(Path.GetTempPath(), $"rt-mcp-config-{Guid.NewGuid():N}");
            var home = Path.Combine(root, "home");
            var repository = Path.Combine(root, "repo");
            var marker = Path.Combine(repository, ".git");
            var config = Path.Combine(repository, ".rt", "config.json");
            Directory.CreateDirectory(marker);
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            Directory.CreateDirectory(home);
            File.WriteAllText(config, json);
            var loader = new HostConfigurationLoader(
                _ => null,
                candidate => string.Equals(candidate, marker, StringComparison.Ordinal));
            return new ConfigurationFixture(root, config, loader.Resolve(repository, home));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
