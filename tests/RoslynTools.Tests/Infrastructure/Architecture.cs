using System.Diagnostics;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     Architecture validation tests using rt's own analysis.
///     Validates that the codebase follows dependency rules (no circular deps, proper layering).
/// </summary>
public class Architecture
{
    [Fact(DisplayName = "Core and Services namespaces must not have circular dependency")]
    public void core_and_services_must_not_have_circular_dependency()
    {
        // ARRANGE: Use rt circular-deps to check for namespace cycles
        var psi = new ProcessStartInfo
        {
            FileName = "rt.cmd",
            Arguments = "circular-deps --solution RoslynTools.slnx",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = FindSolutionRoot()
        };

        // ACT: Run rt circular-deps
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        // ASSERT: No circular dependency must exist between Core and Services.
        // The cycle RoslynTools.Core -> RoslynTools.Services -> RoslynTools.Core
        // has been eliminated. rt circular-deps reports "No circular namespace dependencies found."
        // when the solution is clean, or lists cycles (one per line) if they exist.
        Assert.True(
            !stdout.Contains("->", StringComparison.Ordinal),
            $"Circular dependency still exists: {stdout.Trim()}");
    }

    private static string FindSolutionRoot()
    {
        // Walk up from the test assembly until we find RoslynTools.slnx
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoslynTools.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate RoslynTools.slnx solution root.");
    }
}
