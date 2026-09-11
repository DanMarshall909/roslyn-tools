using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools;

internal sealed class DoctorWorkspaceBootstrap : IDoctorWorkspaceBootstrap
{
    public Result<bool, RtError> Register(string solutionPath)
    {
        try
        {
            MsBuildBootstrap.Register();
            return Result<bool, RtError>.Ok(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException)
        {
            return Result<bool, RtError>.Err(RtError.ExternalToolFailure(
                $"MSBuild registration failed before loading '{solutionPath}': {exception.Message}"));
        }
    }
}

internal sealed class DoctorEnvironmentProbe(
    string? cliLauncher = null,
    string? mcpLauncher = null,
    string? buildHostDirectory = null,
    TimeSpan? timeout = null,
    Func<string, DoctorSdkSelection>? compatibleSdkProbe = null) : IDoctorEnvironmentProbe
{
    private const string ExpectedProtocol = "2024-11-05";
    private const string ExpectedServer = "roslyn-tools-mcp";
    private readonly string? _cliLauncher = cliLauncher ?? Environment.ProcessPath;
    private readonly string? _mcpLauncher = mcpLauncher;
    private readonly string? _buildHostDirectory = buildHostDirectory;
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(10);
    private readonly Func<string, DoctorSdkSelection> _compatibleSdkProbe =
        compatibleSdkProbe ?? (directory => SelectCompatibleSdk(directory));

    public async Task<DoctorEnvironmentProbeResult> ProbeAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var cliHealthy = IsExecutableFile(_cliLauncher);
        if (!cliHealthy)
            diagnostics.Add("The installed rt launcher could not be resolved. Reinstall Roslyn Tools.");

        var mcpPath = _mcpLauncher ?? ResolveSiblingMcp(_cliLauncher);
        var mcpHealthy = IsExecutableFile(mcpPath);
        if (!mcpHealthy)
            diagnostics.Add($"The sibling MCP launcher was not found at '{mcpPath ?? "unknown"}'. Reinstall Roslyn Tools.");

        var buildHostRoot = _buildHostDirectory ?? ResolveBuildHostDirectory();
        var buildHostHealthy = HasBuildHostContents(buildHostRoot);
        if (!buildHostHealthy)
            diagnostics.Add($"Roslyn BuildHost-netcore contents are incomplete at '{buildHostRoot}'. Reinstall Roslyn Tools.");

        var sdk = _compatibleSdkProbe(directory);
        if (!sdk.Compatible)
            diagnostics.Add(
                sdk.Diagnostic
                ?? $"No compatible .NET SDK could be selected for global.json request "
                + $"'{sdk.RequestedVersion ?? "unknown"}' (selected '{sdk.SelectedVersion ?? "none"}'). "
                + "Install a compatible SDK and ensure dotnet is on PATH.");

        var handshake = mcpHealthy
            ? await ProbeMcpAsync(mcpPath!, cancellationToken)
            : McpProbe.Failed;
        diagnostics.AddRange(handshake.Diagnostics);

        return new DoctorEnvironmentProbeResult(
            cliHealthy,
            mcpHealthy,
            buildHostHealthy,
            sdk.Compatible,
            handshake.Healthy,
            handshake.ProtocolVersion,
            handshake.ServerName,
            handshake.DiscoveredTools,
            sdk.RequestedVersion,
            sdk.SelectedVersion,
            diagnostics,
            sdk.RequestState.ToString(),
            sdk.GlobalJsonPath,
            sdk.Diagnostic);
    }

    private async Task<McpProbe> ProbeMcpAsync(string mcpPath, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = mcpPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("The MCP process did not start.");
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"rt-doctor\",\"version\":\"1.0\"}}}");
            await process.StandardInput.FlushAsync(timeoutSource.Token);
            var initializeLine = await process.StandardOutput.ReadLineAsync(timeoutSource.Token);
            var initializeValidation = ValidateInitializeResponse(initializeLine);
            if (!initializeValidation.Healthy)
            {
                TryKill(process);
                var initializationStderr = await stderrTask;
                var initializationDiagnostics = initializeValidation.Diagnostics.ToList();
                if (!string.IsNullOrWhiteSpace(initializationStderr))
                    initializationDiagnostics.Add($"MCP stderr: {initializationStderr.Trim()}");
                return initializeValidation with { Diagnostics = initializationDiagnostics };
            }

            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}");
            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
            await process.StandardInput.FlushAsync(timeoutSource.Token);
            var toolsLine = await process.StandardOutput.ReadLineAsync(timeoutSource.Token);
            process.StandardInput.Close();

            var remainingStdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
            var remainingStdout = await remainingStdoutTask;
            var stderr = await stderrTask;
            var stdout = string.Join(
                Environment.NewLine,
                new[] { initializeLine, toolsLine, remainingStdout }
                    .Where(line => !string.IsNullOrWhiteSpace(line)));
            return ValidateMcpOutput(stdout, stderr, process.ExitCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new McpProbe(false, null, null, 0, [
                $"MCP protocol handshake timed out after {_timeout.TotalSeconds:0.#} seconds."]);
        }
        catch (Exception exception) when (exception is IOException
                                          or InvalidOperationException
                                          or System.ComponentModel.Win32Exception)
        {
            TryKill(process);
            return new McpProbe(false, null, null, 0, [$"MCP protocol handshake failed: {exception.Message}"]);
        }
        finally
        {
            process?.Dispose();
        }
    }

    internal static McpProbe ValidateInitializeResponse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new McpProbe(false, null, null, 0, ["MCP initialize returned no JSON-RPC response."]);

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.GetProperty("jsonrpc").GetString() != "2.0"
                || root.GetProperty("id").GetInt32() != 1)
                throw new JsonException("initialize response identity was invalid.");
            var result = root.GetProperty("result");
            var protocol = result.GetProperty("protocolVersion").GetString();
            var serverName = result.GetProperty("serverInfo").GetProperty("name").GetString();
            var diagnostics = new List<string>();
            if (!result.TryGetProperty("capabilities", out var capabilities)
                || capabilities.ValueKind != JsonValueKind.Object
                || !capabilities.TryGetProperty("tools", out var toolsCapability)
                || toolsCapability.ValueKind != JsonValueKind.Object)
                diagnostics.Add("MCP initialize result did not advertise object capabilities.tools.");
            if (protocol != ExpectedProtocol)
                diagnostics.Add($"MCP protocol version was '{protocol ?? "missing"}', expected '{ExpectedProtocol}'.");
            if (serverName != ExpectedServer)
                diagnostics.Add($"MCP server name was '{serverName ?? "missing"}', expected '{ExpectedServer}'.");
            return new McpProbe(diagnostics.Count == 0, protocol, serverName, 0, diagnostics);
        }
        catch (Exception exception) when (exception is JsonException
                                          or InvalidOperationException
                                          or KeyNotFoundException
                                          or FormatException)
        {
            return new McpProbe(false, null, null, 0, [
                $"MCP initialize response was not valid JSON-RPC: {exception.Message}"]);
        }
    }

    internal static McpProbe ValidateMcpOutput(string stdout, string stderr, int exitCode)
    {
        var diagnostics = new List<string>();
        string? protocol = null;
        string? serverName = null;
        var toolCount = 0;
        var responses = 0;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.GetProperty("jsonrpc").GetString() != "2.0")
                    throw new JsonException("jsonrpc was not 2.0");
                var id = root.GetProperty("id").GetInt32();
                var result = root.GetProperty("result");
                if (id == 1)
                {
                    protocol = result.GetProperty("protocolVersion").GetString();
                    serverName = result.GetProperty("serverInfo").GetProperty("name").GetString();
                }
                else if (id == 2)
                {
                    var tools = result.GetProperty("tools").EnumerateArray().ToArray();
                    toolCount = tools.Length;
                    if (tools.Any(tool => tool.GetProperty("inputSchema").ValueKind != JsonValueKind.Object))
                        diagnostics.Add("MCP tools/list returned a tool without an object input schema.");
                    if (tools.Any(tool => tool.TryGetProperty("outputSchema", out var schema)
                                          && schema.ValueKind != JsonValueKind.Object))
                        diagnostics.Add("MCP tools/list returned a non-object output schema.");
                }
                else
                {
                    diagnostics.Add($"MCP stdout returned unexpected JSON-RPC response id {id}.");
                }

                responses++;
            }
            catch (Exception exception) when (exception is JsonException
                                              or InvalidOperationException
                                              or KeyNotFoundException
                                              or FormatException)
            {
                diagnostics.Add($"MCP stdout was not pure JSON-RPC: {exception.Message}");
            }
        }

        if (responses != 2)
            diagnostics.Add($"MCP handshake expected two JSON-RPC responses but received {responses}.");
        if (protocol != ExpectedProtocol)
            diagnostics.Add($"MCP protocol version was '{protocol ?? "missing"}', expected '{ExpectedProtocol}'.");
        if (serverName != ExpectedServer)
            diagnostics.Add($"MCP server name was '{serverName ?? "missing"}', expected '{ExpectedServer}'.");
        if (toolCount == 0)
            diagnostics.Add("MCP tools/list returned no discoverable tools.");
        if (exitCode != 0)
            diagnostics.Add($"MCP launcher exited with code {exitCode}.");
        if (!string.IsNullOrWhiteSpace(stderr))
            diagnostics.Add($"MCP stderr: {stderr.Trim()}");

        return new McpProbe(diagnostics.Count == 0, protocol, serverName, toolCount, diagnostics);
    }

    private static bool IsExecutableFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static string? ResolveSiblingMcp(string? cliPath)
    {
        var directory = Path.GetDirectoryName(cliPath);
        return string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.Combine(directory, OperatingSystem.IsWindows() ? "roslyn-tools-mcp.exe" : "roslyn-tools-mcp");
    }

    private static string ResolveBuildHostDirectory()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(MSBuildWorkspace).Assembly.Location)
                                ?? AppContext.BaseDirectory;
        return Path.Combine(assemblyDirectory, "BuildHost-netcore");
    }

    private static bool HasBuildHostContents(string path) =>
        File.Exists(Path.Combine(path, "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll"))
        && File.Exists(Path.Combine(path, "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.runtimeconfig.json"))
        && File.Exists(Path.Combine(path, "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.deps.json"));

    internal static DoctorSdkSelection SelectCompatibleSdk(
        string directory,
        string dotnetLauncher = "dotnet")
    {
        var request = MsBuildBootstrap.InspectSdkRequest(directory);
        if (request.State == DoctorSdkRequestState.Invalid)
            return new DoctorSdkSelection(
                false,
                null,
                null,
                request.State,
                request.GlobalJsonPath,
                request.Diagnostic);

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnetLauncher,
                WorkingDirectory = Directory.Exists(directory) ? directory : AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");
            process = Process.Start(startInfo);
            if (process is null || !process.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds))
            {
                TryKill(process);
                return CreateFailedSdkSelection(request, "dotnet SDK selection timed out.");
            }

            var selected = process.StandardOutput.ReadToEnd().Trim();
            var stderr = process.StandardError.ReadToEnd().Trim();
            var compatible = request.State == DoctorSdkRequestState.Absent
                             || process.ExitCode == 0 && !string.IsNullOrWhiteSpace(selected);
            var diagnostic = compatible
                ? null
                : $"dotnet could not select SDK '{request.RequestedVersion}' requested by "
                  + $"'{request.GlobalJsonPath}': {stderr}";
            return new DoctorSdkSelection(
                compatible,
                request.RequestedVersion,
                string.IsNullOrWhiteSpace(selected) ? null : selected,
                request.State,
                request.GlobalJsonPath,
                diagnostic);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or System.ComponentModel.Win32Exception
                                          or IOException)
        {
            return CreateFailedSdkSelection(
                request,
                $"dotnet SDK selection could not start: {exception.Message}");
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static DoctorSdkSelection CreateFailedSdkSelection(
        DoctorSdkRequest request,
        string diagnostic) => new(
        request.State == DoctorSdkRequestState.Absent,
        request.RequestedVersion,
        null,
        request.State,
        request.GlobalJsonPath,
        diagnostic);

    private static void TryKill(Process? process)
    {
        if (process is null || process.HasExited)
            return;

        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    internal sealed record McpProbe(
        bool Healthy,
        string? ProtocolVersion,
        string? ServerName,
        int DiscoveredTools,
        IReadOnlyList<string> Diagnostics)
    {
        internal static McpProbe Failed { get; } = new(false, null, null, 0, []);
    }
}

internal sealed record DoctorSdkSelection(
    bool Compatible,
    string? RequestedVersion,
    string? SelectedVersion,
    DoctorSdkRequestState RequestState = DoctorSdkRequestState.Absent,
    string? GlobalJsonPath = null,
    string? Diagnostic = null);
