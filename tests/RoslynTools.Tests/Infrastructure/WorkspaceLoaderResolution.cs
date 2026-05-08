using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

public class WorkspaceLoaderResolution
{
    static WorkspaceLoaderResolution()
    {
        MsBuildFixture.Ensure();
    }

    [Fact]
    public async Task child_solution_is_chosen_when_it_is_the_nearest_match()
    {
        using var project = new WorkspaceLoaderTestProject();
        project.AddSolution("", "RepoRoot", "RepoRootProject");
        var child = project.AddSolution("src", "Feature", "FeatureProject");
        var workingDir = project.CreateDirectory(Path.Combine("src", "feature", "nested"));

        using (project.UseCurrentDirectory(workingDir))
        {
            var (workspace, solution) = await WorkspaceLoader.LoadAsync(null);
            using (workspace)
            {
                Assert.Equal(child.SolutionPath, solution.FilePath);
                Assert.Contains(solution.Projects, p => p.Name == child.ProjectName);
            }
        }
    }

    [Fact]
    public async Task parent_solution_is_used_when_current_directory_has_no_solution()
    {
        using var project = new WorkspaceLoaderTestProject();
        var parent = project.AddSolution("", "RepoRoot", "RepoRootProject");
        var workingDir = project.CreateDirectory(Path.Combine("tools", "scripts"));

        using (project.UseCurrentDirectory(workingDir))
        {
            var (workspace, solution) = await WorkspaceLoader.LoadAsync(null);
            using (workspace)
            {
                Assert.Equal(parent.SolutionPath, solution.FilePath);
                Assert.Contains(solution.Projects, p => p.Name == parent.ProjectName);
            }
        }
    }

    [Fact]
    public async Task handles_ambiguous_nearest_solution_level_requires_explicit_override()
    {
        using var project = new WorkspaceLoaderTestProject();
        project.AddSolution("", "Alpha", "AlphaProject");
        project.AddSolution("", "Beta", "BetaProject");
        var workingDir = project.CreateDirectory(Path.Combine("src", "feature"));

        using (project.UseCurrentDirectory(workingDir))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceLoader.LoadAsync(null));

            Assert.Contains("Alpha.sln", ex.Message);
            Assert.Contains("Beta.sln", ex.Message);
            Assert.Contains("--solution", ex.Message);
        }
    }

    [Fact]
    public async Task handles_missing_solution_reports_override_guidance()
    {
        using var project = new WorkspaceLoaderTestProject();
        var workingDir = project.CreateDirectory(Path.Combine("src", "feature"));

        using (project.UseCurrentDirectory(workingDir))
        {
            var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => WorkspaceLoader.LoadAsync(null));

            Assert.Contains("No .sln", ex.Message);
            Assert.Contains("--solution", ex.Message);
        }
    }

    [Fact]
    public async Task repo_default_takes_precedence_over_auto_detection()
    {
        using var project = new WorkspaceLoaderTestProject();
        var preferred = project.AddSolution("eng", "Preferred", "PreferredProject");
        project.AddSolution("src", "Nearest", "NearestProject");
        project.WriteRepoDefault(Path.Combine("eng", "Preferred.sln"));
        var workingDir = project.CreateDirectory(Path.Combine("src", "feature", "nested"));

        using (project.UseCurrentDirectory(workingDir))
        {
            var (workspace, solution) = await WorkspaceLoader.LoadAsync(null);
            using (workspace)
            {
                Assert.Equal(preferred.SolutionPath, solution.FilePath);
                Assert.Contains(solution.Projects, p => p.Name == preferred.ProjectName);
            }
        }
    }

    [Theory]
    [InlineData("missing.sln", false)]
    [InlineData("eng\\Preferred.csproj", true)]
    public async Task reports_invalid_repo_default_configuration_without_falling_back(
        string configuredPath,
        bool createTargetFile)
    {
        using var project = new WorkspaceLoaderTestProject();
        project.AddSolution("src", "Nearest", "NearestProject");
        project.WriteRepoDefault(configuredPath);
        if (createTargetFile)
        {
            var invalidTarget = Path.Combine(project.RootDir, configuredPath);
            Directory.CreateDirectory(Path.GetDirectoryName(invalidTarget)!);
            File.WriteAllText(invalidTarget, "<Project />");
        }

        var workingDir = project.CreateDirectory(Path.Combine("src", "feature"));
        using (project.UseCurrentDirectory(workingDir))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceLoader.LoadAsync(null));

            Assert.Contains(".rt-solution", ex.Message);
            Assert.Contains(configuredPath, ex.Message);
        }
    }

    [Fact]
    public async Task uses_explicit_solution_over_repo_default()
    {
        using var project = new WorkspaceLoaderTestProject();
        project.WriteRepoDefault(Path.Combine("eng", "Preferred.sln"));
        project.AddSolution("eng", "Preferred", "PreferredProject");
        var explicitSolution = project.AddSolution("src", "Chosen", "ChosenProject");
        var workingDir = project.CreateDirectory(Path.Combine("src", "feature"));

        using (project.UseCurrentDirectory(workingDir))
        {
            var (workspace, solution) = await WorkspaceLoader.LoadAsync(explicitSolution.SolutionPath);
            using (workspace)
            {
                Assert.Equal(explicitSolution.SolutionPath, solution.FilePath);
                Assert.Contains(solution.Projects, p => p.Name == explicitSolution.ProjectName);
            }
        }
    }

    [Fact]
    public async Task accepts_and_attempts_explicit_slnx_path()
    {
        using var project = new WorkspaceLoaderTestProject();
        var slnxPath = Path.Combine(project.RootDir, "RoslynTools.slnx");
        File.WriteAllText(slnxPath, "{}");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => WorkspaceLoader.LoadAsync(slnxPath));

        Assert.DoesNotContain("unsupported format", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MSBuildWorkspace does not support .slnx", ex.Message);
    }

    [Fact]
    public async Task handles_single_slnx_file_is_auto_detected()
    {
        using var project = new WorkspaceLoaderTestProject();
        var slnxPath = Path.Combine(project.RootDir, "RoslynTools.slnx");
        File.WriteAllText(slnxPath, "{}");

        using (project.UseCurrentDirectory(project.RootDir))
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => WorkspaceLoader.LoadAsync(null));

            Assert.DoesNotContain("No .sln/.slnx", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("MSBuildWorkspace does not support .slnx", ex.Message);
        }
    }
}
