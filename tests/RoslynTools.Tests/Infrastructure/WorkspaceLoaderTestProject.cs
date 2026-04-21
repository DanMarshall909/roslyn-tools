using System.Text;

namespace RoslynTools.Tests.Infrastructure;

internal sealed class WorkspaceLoaderTestProject : IDisposable
{
    private readonly string _rootDir;

    public WorkspaceLoaderTestProject()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), $"rt-workspace-loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDir);
    }

    public string RootDir => _rootDir;

    public SolutionSpec AddSolution(
        string relativeDirectory,
        string solutionName,
        string projectName,
        string? source = null)
    {
        var solutionDir = Path.GetFullPath(Path.Combine(_rootDir, relativeDirectory));
        var projectDir = Path.Combine(solutionDir, projectName);
        Directory.CreateDirectory(projectDir);

        var projectPath = Path.Combine(projectDir, $"{projectName}.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """,
            Encoding.UTF8);

        var sourcePath = Path.Combine(projectDir, "Marker.cs");
        File.WriteAllText(
            sourcePath,
            source ?? $$"""
            namespace {{projectName}};

            public class Marker
            {
            }
            """,
            Encoding.UTF8);

        var solutionPath = Path.Combine(solutionDir, $"{solutionName}.sln");
        var projectGuid = Guid.NewGuid().ToString("D").ToUpperInvariant();
        File.WriteAllText(
            solutionPath,
            $$"""

            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "{{projectName}}", "{{projectName}}\{{projectName}}.csproj", "{{{projectGuid}}}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{{{projectGuid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{{{projectGuid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU
            	EndGlobalSection
            EndGlobal
            """.TrimStart(),
            Encoding.UTF8);

        return new SolutionSpec(solutionPath, projectName);
    }

    public string CreateDirectory(string relativeDirectory)
    {
        var directory = Path.Combine(_rootDir, relativeDirectory);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void WriteRepoDefault(string relativeSolutionPath)
    {
        File.WriteAllText(Path.Combine(_rootDir, ".rt-solution"), relativeSolutionPath + Environment.NewLine, Encoding.UTF8);
    }

    public IDisposable UseCurrentDirectory(string directory)
    {
        var target = Path.IsPathRooted(directory)
            ? Path.GetFullPath(directory)
            : Path.GetFullPath(Path.Combine(_rootDir, directory));

        if (!target.StartsWith(_rootDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Directory '{directory}' must stay within '{_rootDir}'.");

        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(target);
        return new CurrentDirectoryScope(previous);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDir))
                Directory.Delete(_rootDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temporary workspace trees.
        }
    }

    internal sealed record SolutionSpec(string SolutionPath, string ProjectName);

    private sealed class CurrentDirectoryScope(string previousDirectory) : IDisposable
    {
        public void Dispose() => Directory.SetCurrentDirectory(previousDirectory);
    }
}
