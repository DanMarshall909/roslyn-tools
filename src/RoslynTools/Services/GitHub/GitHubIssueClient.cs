using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynTools.Services.GitHub;

/// <summary>The real <see cref="IGitHubIssueClient" />: shells the <c>gh</c> CLI via <see cref="Process" />.</summary>
internal sealed class GitHubIssueClient : IGitHubIssueClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public bool IsAvailable()
    {
        try
        {
            return Run("auth", "status").ExitCode == 0;
        }
        catch (Exception)
        {
            // Contract: a probe method, not a command — any reason gh couldn't be reached
            // (not on PATH, failed to start, a broken pipe under I/O interference, ...) means
            // "unavailable," which the caller already surfaces as a clean ExternalToolFailure.
            // Catching broadly here (rather than just InvalidOperationException) is what makes
            // that translation happen for every failure mode, not only the ones this file
            // happens to wrap as InvalidOperationException. Deliberately not narrower: this
            // method takes no CancellationToken, so there is no OperationCanceledException to
            // let through, and it never hides the failure — it maps to a clean, actionable
            // RtError one level up rather than a raw stack trace.
            return false;
        }
    }

    public IReadOnlyList<GitHubIssueSummary> Search(string query, int limit)
    {
        var repo = ResolveRepo();
        var result = Run(
            "issue", "list",
            "--repo", repo,
            "--search", query,
            "--state", "all",
            "--limit", limit.ToString(CultureInfo.InvariantCulture),
            "--json", "number,title,url,body,state");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"gh issue list failed: {FirstLine(result.StdErr)}");

        return JsonSerializer.Deserialize<List<GitHubIssueSummary>>(result.StdOut, JsonOpts) ?? [];
    }

    public GitHubIssueSummary GetIssue(int number)
    {
        var repo = ResolveRepo();
        var result = Run(
            "issue", "view", number.ToString(CultureInfo.InvariantCulture),
            "--repo", repo,
            "--json", "number,title,url,body,state");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"gh issue view failed: {FirstLine(result.StdErr)}");

        return JsonSerializer.Deserialize<GitHubIssueSummary>(result.StdOut, JsonOpts)
            ?? throw new InvalidOperationException($"gh issue view #{number} returned no data.");
    }

    public GitHubIssueSummary CreateIssue(string title, string body)
    {
        var repo = ResolveRepo();
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, body);
            var result = Run(
                "issue", "create",
                "--repo", repo,
                "--title", title,
                "--body-file", tmp);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"gh issue create failed: {FirstLine(result.StdErr)}");

            // `gh issue create` prints only the created issue's URL on success, but tolerate
            // extra lines (e.g. an update notice) by taking the last non-empty one, and fail
            // cleanly (caught by FeedbackReportOperation as ExternalToolFailure) rather than
            // letting an unparsable number escape as an unhandled FormatException.
            var url = result.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r').Trim())
                .LastOrDefault(line => line.Length > 0) ?? "";
            if (!int.TryParse(
                    url[(url.LastIndexOf('/') + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var number))
                throw new InvalidOperationException(
                    $"gh issue create succeeded but its output did not contain an issue URL: '{result.StdOut.Trim()}'");

            return new GitHubIssueSummary(number, title, url, body, "OPEN");
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    /// <summary>
    ///     Re-resolved on every call rather than cached: this client is registered as a
    ///     singleton (see Bootstrap), and it is scoped to "the process's current working
    ///     directory" at call time, not at first use — caching would go stale if that ever
    ///     changed mid-process. The extra `gh repo view` per call is negligible for a
    ///     human-initiated action like `rt feedback report`.
    /// </summary>
    private static string ResolveRepo()
    {
        var result = Run("repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"gh repo view failed: {FirstLine(result.StdErr)}");
        return result.StdOut.Trim();
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimEnd('\r') ?? text;

    private static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("gh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start gh process");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("gh CLI was not found on PATH.", ex);
        }

        using (proc)
        {
            // Read stdout and stderr concurrently, not sequentially: `gh issue list --json`
            // can return many full issue bodies on stdout, and gh is known to write update
            // notices to stderr. Draining one stream fully before starting the other risks
            // the classic Process redirect deadlock if the un-drained stream's OS pipe buffer
            // fills while gh is blocked writing to it.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            Task.WaitAll(stdoutTask, stderrTask);
            proc.WaitForExit();
            return (proc.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
    }
}
