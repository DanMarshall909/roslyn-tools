using System.Diagnostics;
using System.Text.Json;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public sealed class SuggestGitContextSurfaceTests
{
    [Fact]
    public async Task omitted_query_outside_git_reports_an_error_while_queried_discovery_still_works()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"rt-suggest-no-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var text = await RtCliHost.RunAsync("suggest", directory);
            var json = await RtCliHost.RunAsync("suggest --json", directory);
            var queried = await RtCliHost.RunAsync("suggest \"find references\" --json", directory);

            text.ExitCode.ShouldBe(1);
            text.Stdout.ShouldBeEmpty();
            text.Stderr.ShouldContain("Git context", Case.Insensitive);
            text.Stderr.ShouldContain("query", Case.Insensitive);
            json.ExitCode.ShouldBe(1);
            json.Stdout.ShouldBeEmpty();
            json.Stderr.ShouldContain("Git context", Case.Insensitive);
            queried.ExitCode.ShouldBe(0, queried.Stderr);
            using var payload = JsonDocument.Parse(queried.Stdout);
            payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBeGreaterThan(0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task omitted_query_requires_a_commit_before_claiming_a_clean_tree()
    {
        using var repository = new TemporaryRepository();
        repository.Write("New.cs", "class New { }");

        var result = await RtCliHost.RunAsync("suggest --json", repository.Path);

        result.ExitCode.ShouldBe(1);
        result.Stdout.ShouldBeEmpty();
        result.Stderr.ShouldContain("HEAD", Case.Sensitive);
        result.Stderr.ShouldContain("query", Case.Insensitive);
    }

    [Fact]
    public async Task omitted_query_in_a_clean_repository_reports_zero_changed_csharp_files()
    {
        using var repository = new TemporaryRepository();
        repository.Write("Baseline.cs", "class Baseline { }");
        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "baseline");

        var result = await RtCliHost.RunAsync("suggest --json", repository.Path);

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("context").GetString().ShouldBe("0 .cs file(s) changed vs HEAD");
    }

    [Fact]
    public async Task omitted_query_counts_tracked_and_untracked_csharp_changes()
    {
        using var repository = new TemporaryRepository();
        repository.Write("Tracked.cs", "class Tracked { }");
        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "baseline");
        repository.Write("Tracked.cs", "class Tracked { int Value; }");
        repository.Write("Untracked.cs", "class Untracked { }");

        var result = await RtCliHost.RunAsync("suggest --json", repository.Path);

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("context").GetString().ShouldBe("2 .cs file(s) changed vs HEAD");
    }

    [Fact]
    public async Task omitted_query_reports_all_changed_csharp_files_even_when_display_is_limited()
    {
        using var repository = new TemporaryRepository();
        repository.Write("Baseline.cs", "class Baseline { }");
        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "baseline");
        for (var index = 0; index < 37; index++)
            repository.Write($"Changed{index:D2}.cs", $"class Changed{index:D2} {{ }}");
        repository.Write("Notes.txt", "not C#");

        var json = await RtCliHost.RunAsync("suggest --top 3 --json", repository.Path);
        var text = await RtCliHost.RunAsync("suggest --top 3", repository.Path);
        var zero = await RtCliHost.RunAsync("suggest --top 0 --json", repository.Path);
        var zeroText = await RtCliHost.RunAsync("suggest --top 0", repository.Path);

        json.ExitCode.ShouldBe(0, json.Stderr);
        using var payload = JsonDocument.Parse(json.Stdout);
        payload.RootElement.GetProperty("context").GetString().ShouldBe("37 .cs file(s) changed vs HEAD");
        var suggestions = payload.RootElement.GetProperty("suggestions").EnumerateArray()
            .Select(item => item.GetString()).ToList();
        suggestions.Count(item => item?.StartsWith("rt inspect outline ", StringComparison.Ordinal) == true)
            .ShouldBe(3);
        suggestions.Last().ShouldBe("rt dump-help --compact");

        text.ExitCode.ShouldBe(0, text.Stderr);
        text.Stdout.ShouldContain("Context: 37 changed .cs file(s)");
        text.Stdout.Split('\n').Count(line => line.StartsWith("rt inspect outline ", StringComparison.Ordinal))
            .ShouldBe(3);

        zero.ExitCode.ShouldBe(0, zero.Stderr);
        using var zeroPayload = JsonDocument.Parse(zero.Stdout);
        zeroPayload.RootElement.GetProperty("context").GetString().ShouldBe("37 .cs file(s) changed vs HEAD");
        zeroPayload.RootElement.GetProperty("suggestions").GetArrayLength().ShouldBe(1);

        zeroText.ExitCode.ShouldBe(0, zeroText.Stderr);
        zeroText.Stdout.ShouldContain("Context: 37 changed .cs file(s)");
        zeroText.Stdout.ShouldNotContain("No changed .cs files found");
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rt-suggest-git-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            Git("init", "-q");
            Git("config", "user.email", "rt-test@example.com");
            Git("config", "user.name", "rt test");
            Git("config", "commit.gpgsign", "false");
            Git("config", "core.hooksPath", "");
        }

        public string Path { get; }

        public void Write(string name, string contents) =>
            File.WriteAllText(System.IO.Path.Combine(Path, name), contents);

        public void Git(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Failed to start git.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {stderr}{stdout}");
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
