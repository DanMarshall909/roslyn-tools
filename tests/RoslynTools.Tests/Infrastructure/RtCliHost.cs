using System.Diagnostics;
using RoslynTools.Daemon.Rpc;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     Launches the built <c>rt.dll</c> as a real subprocess for end-to-end CLI tests.
///     Resolves both the build configuration and target framework from the currently
///     running test assembly's own <c>bin/&lt;Configuration&gt;/&lt;TFM&gt;/</c> path rather
///     than hardcoding either — CI builds Release while local dev builds Debug, and a
///     hardcoded value only ever matches one of them (rt-006).
/// </summary>
internal static class RtCliHost
{
    internal static string RepoRoot { get; } = FindRepoRoot();

    internal static string RtDllPath { get; } = FindRtDll(RepoRoot);

    internal static async Task<CommandResult> RunAsync(
        string arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{RtDllPath}\" {arguments}",
            WorkingDirectory = workingDirectory ?? RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (environment is not null)
            foreach (var (name, value) in environment)
                if (value is null)
                    startInfo.Environment.Remove(name);
                else
                    startInfo.Environment[name] = value;

        using var process = Process.Start(startInfo)
                             ?? throw new InvalidOperationException("Failed to start 'dotnet'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string FindRtDll(string repoRoot)
    {
        var binDir = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = binDir.Name;
        var configuration = binDir.Parent?.Name
                             ?? throw new InvalidOperationException(
                                 "Could not determine build configuration from the test assembly's path.");

        return Path.Combine(repoRoot, "src", "RoslynTools.Cli", "bin", configuration, targetFramework, "rt.dll");
    }
}

internal sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
