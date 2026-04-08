using System.Diagnostics;

namespace RoslynTools.Tests;

/// <summary>
/// Generates a throwaway .NET project + solution from inline C# strings.
/// Each test gets an isolated temp directory with a buildable solution.
/// </summary>
public sealed class TestProjectBuilder : IDisposable
{
    private static readonly string BaseOutputDir =
        Path.Combine(Path.GetDirectoryName(typeof(TestProjectBuilder).Assembly.Location)!, "TestOutput");

    private readonly string _dir;
    private readonly string _projectName;
    private readonly List<(string RelativePath, string Content)> _files = new();

    private string? _solutionPath;
    public string SolutionPath => _solutionPath ?? Path.Combine(_dir, _projectName + ".sln");
    public string ProjectDir => Path.Combine(_dir, _projectName);
    public string Directory => _dir;

    public TestProjectBuilder(string? projectName = null)
    {
        _projectName = projectName ?? "TestProject";
        _dir = Path.Combine(BaseOutputDir, Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Add a .cs file to the project. Path is relative to the project directory.
    /// </summary>
    public TestProjectBuilder AddSource(string relativePath, string content)
    {
        _files.Add((relativePath, content));
        return this;
    }

    /// <summary>
    /// Write all files, create .csproj and .sln, run dotnet build.
    /// Returns the path to the .sln file.
    /// </summary>
    public async Task<string> BuildAsync()
    {
        var projectDir = Path.Combine(_dir, _projectName);
        System.IO.Directory.CreateDirectory(projectDir);

        // Write .csproj
        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(Path.Combine(projectDir, _projectName + ".csproj"), csproj);

        // Write source files
        foreach (var (relativePath, content) in _files)
        {
            var fullPath = Path.Combine(projectDir, relativePath);
            var dir = Path.GetDirectoryName(fullPath)!;
            System.IO.Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(fullPath, content);
        }

        // Write classic .sln (Roslyn 4.9.2 MSBuildWorkspace doesn't support .slnx)
        _solutionPath = Path.Combine(_dir, _projectName + ".sln");
        var projectGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var csprojRelative = _projectName + "\\" + _projectName + ".csproj";
        var slnContent = WriteSln(projectGuid, _projectName, csprojRelative);
        await File.WriteAllTextAsync(_solutionPath, slnContent);

        // Build to produce compilation
        await RunDotnetAsync(_dir, "build", Path.GetFileName(_solutionPath), "-v", "quiet");

        return _solutionPath;
    }

    /// <summary>
    /// Read the current content of a source file (after refactoring has modified it).
    /// </summary>
    public async Task<string> ReadSourceAsync(string relativePath)
    {
        var fullPath = Path.Combine(_dir, _projectName, relativePath);
        return await File.ReadAllTextAsync(fullPath);
    }

    /// <summary>
    /// Check if a source file exists (useful for move tests).
    /// </summary>
    public bool SourceExists(string relativePath)
    {
        return File.Exists(Path.Combine(_dir, _projectName, relativePath));
    }

    private static async Task RunDotnetAsync(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet {string.Join(' ', args)} failed (exit {proc.ExitCode}):\n{stderr}\n{stdout}");
    }

    private static string WriteSln(string projectGuid, string projectName, string csprojRelative)
    {
        const string typeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
        return string.Join("\r\n",
            "",
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            $"Project(\"{typeGuid}\") = \"{projectName}\", \"{csprojRelative}\", \"{projectGuid}\"",
            "EndProject",
            "Global",
            "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution",
            "\t\tDebug|Any CPU = Debug|Any CPU",
            "\tEndGlobalSection",
            "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution",
            $"\t\t{projectGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
            $"\t\t{projectGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU",
            "\tEndGlobalSection",
            "EndGlobal",
            "");
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(_dir))
                System.IO.Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup — CI or next run will clear stale dirs
        }
    }
}
