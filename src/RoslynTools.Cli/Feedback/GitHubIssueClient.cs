using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RoslynTools.Services.GitHub;

/// <summary>GitHub issue client implemented through the authenticated <c>gh</c> CLI.</summary>
internal sealed class GitHubIssueClient : IGitHubIssueClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return (await RunAsync(["auth", "status"], cancellationToken)).ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<GitHubIssueSummary>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var repository = await ResolveRepositoryAsync(cancellationToken);
        var result = await RunAsync(
            [
                "issue", "list",
                "--repo", repository,
                "--search", query,
                "--state", "all",
                "--limit", limit.ToString(CultureInfo.InvariantCulture),
                "--json", "number,title,url,body,state"
            ],
            cancellationToken);
        EnsureSuccess(result, "gh issue list");
        return JsonSerializer.Deserialize<List<GitHubIssueSummary>>(result.StandardOutput, JsonOptions) ?? [];
    }

    public async Task<GitHubIssueSummary> GetIssueAsync(
        int number,
        CancellationToken cancellationToken = default)
    {
        var repository = await ResolveRepositoryAsync(cancellationToken);
        var result = await RunAsync(
            [
                "issue", "view", number.ToString(CultureInfo.InvariantCulture),
                "--repo", repository,
                "--json", "number,title,url,body,state"
            ],
            cancellationToken);
        EnsureSuccess(result, $"gh issue view #{number}");
        return JsonSerializer.Deserialize<GitHubIssueSummary>(result.StandardOutput, JsonOptions)
               ?? throw new InvalidOperationException($"gh issue view #{number} returned no data.");
    }

    public async Task<GitHubIssueSummary> CreateIssueAsync(
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var repository = await ResolveRepositoryAsync(cancellationToken);
        var temporaryFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(temporaryFile, body, cancellationToken);
            var result = await RunAsync(
                [
                    "issue", "create",
                    "--repo", repository,
                    "--title", title,
                    "--body-file", temporaryFile
                ],
                cancellationToken);
            EnsureSuccess(result, "gh issue create");

            var url = result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .LastOrDefault(line => line.Length > 0) ?? "";
            if (!int.TryParse(
                    url[(url.LastIndexOf('/') + 1)..],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var number))
                throw new InvalidOperationException(
                    $"gh issue create succeeded but returned no issue URL: '{result.StandardOutput.Trim()}'");

            return new GitHubIssueSummary(number, title, url, body, "OPEN");
        }
        finally
        {
            try
            {
                File.Delete(temporaryFile);
            }
            catch
            {
                // Best effort cleanup. Reporting success must not become failure after the issue exists.
            }
        }
    }

    private static async Task<string> ResolveRepositoryAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            ["repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"],
            cancellationToken);
        EnsureSuccess(result, "gh repo view");
        return result.StandardOutput.Trim();
    }

    private static void EnsureSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode == 0)
            return;

        var detail = result.StandardError
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "unknown error";
        throw new InvalidOperationException($"{operation} failed: {detail}");
    }

    private static async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("gh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("Failed to start the gh CLI.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("The gh CLI was not found on PATH.", exception);
        }

        using (process)
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(
                process.ExitCode,
                await standardOutput,
                await standardError);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
