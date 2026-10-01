using System.Diagnostics;
using System.Globalization;
using System.Text;
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
    [InlineData("2026-07-28")]
    public async Task spawned_server_reports_protocol_faults_and_recovers(
        string protocolVersion)
    {
        using var process = StartServer();
        var stderr = process.StandardError.ReadToEndAsync();
        var modern = protocolVersion == "2026-07-28";
        var metadata = new Dictionary<string, object>
        {
            ["io.modelcontextprotocol/protocolVersion"] = protocolVersion,
            ["io.modelcontextprotocol/clientInfo"] = new { name = "rt-edge-test", version = "1.0" },
            ["io.modelcontextprotocol/clientCapabilities"] = new { }
        };
        try
        {
            if (modern)
                await SendAsync(process, new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "server/discover",
                    @params = new { _meta = metadata }
                });
            else
                await SendAsync(process, new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "rt-edge-test", version = "1.0" }
                    }
                });
            using (var handshake = await ReadResponseAsync(process))
                handshake.RootElement.GetProperty("id").GetInt32().ShouldBe(1);
            if (!modern)
                await SendAsync(process, new { jsonrpc = "2.0", method = "notifications/initialized" });

            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":90,\"method\":\"tools/list\",");
            await process.StandardInput.FlushAsync();
            using (var malformed = await ReadResponseAsync(process))
            {
                malformed.RootElement.GetProperty("id").GetInt32().ShouldBe(90);
                malformed.RootElement.GetProperty("error").GetProperty("code")
                    .GetInt32().ShouldBe(-32700);
            }

            var parameters = modern ? new { _meta = metadata } : null;
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = "edge-id",
                method = "rt/unknown",
                @params = parameters
            });
            using (var unknown = await ReadResponseAsync(process))
            {
                unknown.RootElement.GetProperty("id").GetString().ShouldBe("edge-id");
                unknown.RootElement.GetProperty("error").GetProperty("code")
                    .GetInt32().ShouldBe(-32601);
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 91,
                method = 7,
                @params = parameters
            });
            // SDK 2.2.0 currently emits ParseError (-32700) for this valid JSON
            // with an invalid Request object. JSON-RPC requires InvalidRequest
            // (-32600); RT-012 tracks this upstream classification gap.
            using (var invalidRequest = await ReadResponseAsync(process))
            {
                invalidRequest.RootElement.GetProperty("id").GetInt32().ShouldBe(91);
                invalidRequest.RootElement.GetProperty("error").GetProperty("code")
                    .GetInt32().ShouldBe(-32700);
            }

            var callParameters = new Dictionary<string, object?>
            {
                ["name"] = "suggest_command",
                ["arguments"] = new { top = 1 }
            };
            if (modern)
                callParameters["_meta"] = metadata;
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 92,
                method = "tools/call",
                @params = callParameters
            });
            using (var failure = await ReadResponseAsync(process))
            {
                failure.RootElement.GetProperty("id").GetInt32().ShouldBe(92);
                var result = failure.RootElement.GetProperty("result");
                result.GetProperty("isError").GetBoolean().ShouldBeTrue();
                using var error = JsonDocument.Parse(result.GetProperty("content")[0]
                    .GetProperty("text").GetString()!);
                error.RootElement.GetProperty("kind").GetString().ShouldBe("InvalidInput");
                if (modern)
                    result.GetProperty("resultType").GetString().ShouldBe("complete");
            }

            if (modern)
            {
                var wrongVersionMetadata = new Dictionary<string, object>(metadata)
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2025-11-25"
                };
                await SendAsync(process, new
                {
                    jsonrpc = "2.0",
                    id = 93,
                    method = "tools/list",
                    @params = new { _meta = wrongVersionMetadata }
                });
                using var wrongVersion = await ReadResponseAsync(process);
                wrongVersion.RootElement.GetProperty("id").GetInt32().ShouldBe(93);
                wrongVersion.RootElement.GetProperty("error").GetProperty("code")
                    .GetInt32().ShouldBe(-32600);
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                method = "notifications/unrecognized",
                @params = parameters
            });
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 94,
                method = "tools/list",
                @params = parameters
            });
            using (var recovered = await ReadResponseAsync(process))
            {
                recovered.RootElement.GetProperty("id").GetInt32().ShouldBe(94);
                recovered.RootElement.GetProperty("result").GetProperty("tools")[0]
                    .GetProperty("name").GetString().ShouldBe("prime");
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
    public async Task spawned_server_rejects_stale_and_out_of_range_tool_page_cursors()
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
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "rt-cursor-test", version = "1.0" }
                }
            });
            using (var handshake = await ReadResponseAsync(process))
                handshake.RootElement.GetProperty("id").GetInt32().ShouldBe(1);
            await SendAsync(process, new { jsonrpc = "2.0", method = "notifications/initialized" });
            await SendAsync(process, new { jsonrpc = "2.0", id = 2, method = "tools/list" });
            string validCursor;
            using (var firstPage = await ReadResponseAsync(process))
                validCursor = firstPage.RootElement.GetProperty("result")
                    .GetProperty("nextCursor").GetString()!;

            var parts = validCursor.Split(':');
            parts.Length.ShouldBe(3);
            var count = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var countText = count.ToString(CultureInfo.InvariantCulture);
            var invalidCursors = new[]
            {
                $"rt-tools-v1:{(count + 1).ToString(CultureInfo.InvariantCulture)}:20",
                $"rt-tools-v1:{countText}:0",
                $"rt-tools-v1:{countText}:21",
                $"rt-tools-v1:{countText}:{countText}",
                $"rt-tools-v1:{countText}:{(count + 20).ToString(CultureInfo.InvariantCulture)}"
            };
            var requestId = 3;
            foreach (var cursor in invalidCursors)
            {
                await SendAsync(process, new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "tools/list",
                    @params = new { cursor }
                });
                using var response = await ReadResponseAsync(process);
                response.RootElement.GetProperty("id").GetInt32().ShouldBe(requestId);
                response.RootElement.GetProperty("error").GetProperty("code")
                    .GetInt32().ShouldBe(-32602);
                requestId++;
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = requestId,
                method = "tools/list",
                @params = new { cursor = validCursor }
            });
            using (var recovered = await ReadResponseAsync(process))
                recovered.RootElement.GetProperty("result").GetProperty("tools")
                    .GetArrayLength().ShouldBeGreaterThan(0);

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

    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task spawned_server_pages_the_complete_tool_catalogue_with_valid_schemas(
        string protocolVersion)
    {
        using var process = StartServer();
        var stderr = process.StandardError.ReadToEndAsync();
        var modern = protocolVersion == "2026-07-28";
        var metadata = new Dictionary<string, object>
        {
            ["io.modelcontextprotocol/protocolVersion"] = protocolVersion,
            ["io.modelcontextprotocol/clientInfo"] = new { name = "rt-page-test", version = "1.0" },
            ["io.modelcontextprotocol/clientCapabilities"] = new { }
        };
        try
        {
            if (modern)
                await SendAsync(process, new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "server/discover",
                    @params = new { _meta = metadata }
                });
            else
                await SendAsync(process, new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "rt-page-test", version = "1.0" }
                    }
                });
            using (var handshake = await ReadResponseAsync(process))
                handshake.RootElement.GetProperty("id").GetInt32().ShouldBe(1);
            if (!modern)
                await SendAsync(process, new { jsonrpc = "2.0", method = "notifications/initialized" });

            var names = new List<string>();
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            var requestId = 2;
            do
            {
                var parameters = new Dictionary<string, object?>();
                if (modern)
                    parameters["_meta"] = metadata;
                if (cursor is not null)
                    parameters["cursor"] = cursor;
                await SendAsync(process, new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "tools/list",
                    @params = parameters
                });
                var line = await process.StandardOutput.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10));
                line.ShouldNotBeNullOrWhiteSpace();
                Encoding.UTF8.GetByteCount(line).ShouldBeLessThan(64 * 1024);
                using var page = JsonDocument.Parse(line);
                page.RootElement.GetProperty("id").GetInt32().ShouldBe(requestId);
                var result = page.RootElement.GetProperty("result");
                var tools = result.GetProperty("tools").EnumerateArray().ToArray();
                tools.Length.ShouldBeInRange(1, 20);
                foreach (var tool in tools)
                {
                    var name = tool.GetProperty("name").GetString();
                    name.ShouldNotBeNullOrWhiteSpace();
                    names.Add(name);
                    tool.GetProperty("inputSchema").GetProperty("type")
                        .GetString().ShouldBe("object");
                    tool.GetProperty("outputSchema").GetProperty("oneOf")
                        .GetArrayLength().ShouldBe(2);
                }

                if (requestId == 2)
                {
                    tools[0].GetProperty("name").GetString().ShouldBe("prime");
                    tools[1].GetProperty("name").GetString().ShouldBe("suggest_command");
                }

                cursor = result.TryGetProperty("nextCursor", out var next)
                    ? next.GetString()
                    : null;
                if (cursor is not null)
                    cursors.Add(cursor).ShouldBeTrue("A page cursor must not repeat.");
                requestId++;
            } while (cursor is not null && requestId < 10);

            cursor.ShouldBeNull("The tool catalogue must terminate within eight pages.");
            names.Count.ShouldBe(59);
            names.Distinct(StringComparer.Ordinal).Count().ShouldBe(names.Count);
            names.ShouldContain("prime");
            names.ShouldContain("move_slice");
            names.ShouldContain("semantic_diff");

            var invalidParameters = new Dictionary<string, object?>
            {
                ["cursor"] = "invalid-cursor"
            };
            if (modern)
                invalidParameters["_meta"] = metadata;
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = requestId,
                method = "tools/list",
                @params = invalidParameters
            });
            using (var invalid = await ReadResponseAsync(process))
            {
                invalid.RootElement.GetProperty("id").GetInt32().ShouldBe(requestId);
                invalid.RootElement.GetProperty("error").GetProperty("code")
                    .GetInt32().ShouldBe(-32602);
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
