using System.Diagnostics;

namespace RoslynTools.Tests.Infrastructure;

public class CliBootstrapTests
{
    [Fact]
    public async Task Informational_startup_messages_do_not_use_the_error_stream()
    {
        var repoRoot = FindRepoRoot();
        var rtCmd = Path.Combine(repoRoot, "rt.cmd");

        var result = await RunAsync(rtCmd, "--help", repoRoot);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
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
            UseShellExecute = false,
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
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
}
