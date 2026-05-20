using System.Diagnostics;

namespace RoslynTools.Tests.Infrastructure;

public class CliBootstrap
{
    [Fact]
    public async Task informational_startup_messages_do_not_use_the_error_stream()
    {
        var repoRoot = FindRepoRoot();
        var rtDll = Path.Combine(repoRoot, "src", "RoslynTools.Cli", "bin", "Debug", "net8.0", "rt.dll");

        var result = await RunAsync("dotnet", $"\"{rtDll}\" --help", repoRoot);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("rt Commands", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public async Task missing_solution_path_produces_clean_exit_one_with_no_stack_trace()
    {
        // US-041: when --solution points at a file that doesn't exist, rt must print a single
        // stderr line and exit 1 — no FileNotFoundException stack trace, no "Unhandled exception:"
        // banner from System.CommandLine's default UseExceptionHandler.
        var repoRoot = FindRepoRoot();
        var rtDll = Path.Combine(repoRoot, "src", "RoslynTools.Cli", "bin", "Debug", "net8.0", "rt.dll");
        var missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.slnx");

        var result = await RunAsync(
            "dotnet",
            $"\"{rtDll}\" inspect describe Foo --solution \"{missingPath}\"",
            repoRoot);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("solution not found", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(missingPath, result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unhandled exception", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at System.IO.", result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("at Microsoft.CodeAnalysis.", result.Stderr, StringComparison.Ordinal);
    }

    private static async Task<CommandResult> RunAsync(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CommandResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
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

    private sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
}
