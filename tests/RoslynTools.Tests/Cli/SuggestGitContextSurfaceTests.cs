using System.Diagnostics;
using System.Text.Json;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public sealed class SuggestGitContextSurfaceTests
{
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
