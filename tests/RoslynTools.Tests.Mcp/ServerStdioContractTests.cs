using System.Diagnostics;
using System.Text.Json;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

public sealed class ServerStdioContractTests
{
    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    public async Task spawned_server_negotiates_legacy_clients_and_keeps_stdout_protocol_only(
        string protocolVersion)
    {
        using var process = StartServer();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion,
                    capabilities = new { },
                    clientInfo = new { name = "rt-contract-test", version = "1.0" }
                }
            });
            using (var initialize = await ReadResponseAsync(process))
            {
                initialize.RootElement.GetProperty("id").GetInt32().ShouldBe(1);
                initialize.RootElement.GetProperty("result").GetProperty("protocolVersion")
                    .GetString().ShouldBe(protocolVersion);
                var serverInfo = initialize.RootElement.GetProperty("result")
                    .GetProperty("serverInfo");
                serverInfo.GetProperty("name").GetString().ShouldBe("roslyn-tools-mcp");
                serverInfo.GetProperty("version").GetString().ShouldBe("1.0.0");
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });
            await SendAsync(process, new { jsonrpc = "2.0", id = 2, method = "tools/list" });
            using (var discovery = await ReadResponseAsync(process))
            {
                discovery.RootElement.GetProperty("id").GetInt32().ShouldBe(2);
                discovery.RootElement.GetProperty("result").GetProperty("tools")
                    .EnumerateArray().ShouldContain(tool =>
                        tool.GetProperty("name").GetString() == "prime"
                        && tool.GetProperty("inputSchema").GetProperty("type").GetString()
                        == "object");
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new { name = "prime", arguments = new { } }
            });
            using (var invocation = await ReadResponseAsync(process))
            {
                invocation.RootElement.GetProperty("id").GetInt32().ShouldBe(3);
                var result = invocation.RootElement.GetProperty("result");
                result.GetProperty("isError").GetBoolean().ShouldBeFalse();
                result.GetProperty("structuredContent").GetProperty("hotCommands")
                    .GetArrayLength().ShouldBeGreaterThan(0);
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "tools/call",
                @params = new { name = "suggest_command", arguments = new { top = 1 } }
            });
            using (var failure = await ReadResponseAsync(process))
            {
                failure.RootElement.GetProperty("id").GetInt32().ShouldBe(4);
                var result = failure.RootElement.GetProperty("result");
                result.GetProperty("isError").GetBoolean().ShouldBeTrue();
                using var error = JsonDocument.Parse(result.GetProperty("content")[0]
                    .GetProperty("text").GetString()!);
                error.RootElement.GetProperty("kind").GetString().ShouldBe("InvalidInput");
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 5,
                method = "tools/call",
                @params = new { name = "prime" }
            });
            using (var invocationWithoutArguments = await ReadResponseAsync(process))
            {
                invocationWithoutArguments.RootElement.GetProperty("id").GetInt32()
                    .ShouldBe(5);
                invocationWithoutArguments.RootElement.GetProperty("result")
                    .GetProperty("structuredContent").GetProperty("hotCommands")
                    .GetArrayLength().ShouldBeGreaterThan(0);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            process.ExitCode.ShouldBe(0, await stderr);
            (await process.StandardOutput.ReadToEndAsync()).ShouldBeEmpty();
        }
        finally
        {
            await StopAsync(process);
        }
    }

    [Fact]
    public async Task spawned_server_discovers_and_invokes_tools_with_2026_protocol()
    {
        using var process = StartServer();
        var stderr = process.StandardError.ReadToEndAsync();
        var metadata = new Dictionary<string, object>
        {
            ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
            ["io.modelcontextprotocol/clientInfo"] = new { name = "rt-contract-test", version = "1.0" },
            ["io.modelcontextprotocol/clientCapabilities"] = new { }
        };

        try
        {
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "server/discover",
                @params = new { _meta = metadata }
            });
            using (var discovery = await ReadResponseAsync(process))
            {
                discovery.RootElement.GetProperty("id").GetInt32().ShouldBe(1);
                discovery.RootElement.GetProperty("result").GetProperty("supportedVersions")
                    .EnumerateArray().Select(version => version.GetString())
                    .ShouldContain("2026-07-28");
                var serverInfo = discovery.RootElement.GetProperty("result")
                    .GetProperty("_meta").GetProperty("io.modelcontextprotocol/serverInfo");
                serverInfo.GetProperty("name").GetString().ShouldBe("roslyn-tools-mcp");
                serverInfo.GetProperty("version").GetString().ShouldBe("1.0.0");
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { _meta = metadata }
            });
            using (var list = await ReadResponseAsync(process))
            {
                list.RootElement.GetProperty("id").GetInt32().ShouldBe(2);
                list.RootElement.GetProperty("result").GetProperty("tools")
                    .EnumerateArray().ShouldContain(tool =>
                        tool.GetProperty("name").GetString() == "prime"
                        && tool.GetProperty("inputSchema").GetProperty("type").GetString()
                        == "object"
                        && tool.GetProperty("outputSchema").GetProperty("oneOf")
                            .GetArrayLength() > 0);
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new { _meta = metadata, name = "prime", arguments = new { } }
            });
            using (var invocation = await ReadResponseAsync(process))
            {
                invocation.RootElement.GetProperty("id").GetInt32().ShouldBe(3);
                var result = invocation.RootElement.GetProperty("result");
                result.GetProperty("isError").GetBoolean().ShouldBeFalse();
                result.GetProperty("structuredContent").GetProperty("hotCommands")
                    .GetArrayLength().ShouldBeGreaterThan(0);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            process.ExitCode.ShouldBe(0, await stderr);
            (await process.StandardOutput.ReadToEndAsync()).ShouldBeEmpty();
        }
        finally
        {
            await StopAsync(process);
        }
    }

    [Fact]
    public async Task startup_diagnostics_use_stderr_without_polluting_protocol_stdout()
    {
        using var process = StartServer("--not-a-real-option");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

            process.ExitCode.ShouldBe(2);
            (await stdout).ShouldBeEmpty();
            (await stderr).ShouldContain("Unknown option");
        }
        finally
        {
            await StopAsync(process);
        }
    }

    private static Process StartServer(string? option = null)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = FindRepoRoot(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "RoslynTools.Mcp.dll"));
        if (option is not null)
            startInfo.ArgumentList.Add(option);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the MCP server process.");
    }

    private static async Task SendAsync(Process process, object message)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonDocument> ReadResponseAsync(Process process)
    {
        var line = await process.StandardOutput.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        line.ShouldNotBeNullOrWhiteSpace();
        return JsonDocument.Parse(line);
    }

    private static async Task StopAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
