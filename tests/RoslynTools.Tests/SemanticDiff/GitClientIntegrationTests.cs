using System.Diagnostics;
using System.Text;
using RoslynTools.Services.SemanticDiff.Git;
using Shouldly;

namespace RoslynTools.Tests.SemanticDiff;

/// <summary>
///     Integration coverage for <see cref="GitClient" /> — the real implementation,
///     exercised against a freshly initialised throwaway git repository (US-122 batch 2).
///     The unit-level behaviour of the provider is covered by
///     <see cref="GitSemanticDiffSourceProviderTests" /> with a fake; this proves the
///     real <c>git</c> shelling, path listing, content-at-ref, and the two error channels
///     work end to end.
/// </summary>
public sealed class GitClientIntegrationTests
{
    [Fact]
    public void changed_paths_and_content_at_ref_work_against_a_real_repository()
    {
        using var repo = new TempRepo();

        repo.Write("Gadget.cs", "class Gadget { void V1() { } }");
        repo.Git("add", "-A");
        repo.Commit("first");
        var first = repo.Head();

        repo.Write("Gadget.cs", "class Gadget { void V2() { } }");
        repo.Git("add", "-A");
        repo.Commit("second");
        var second = repo.Head();

        var client = new GitClient(repo.Path);

        client.GetChangedPathsBetween(first, second)
            .ShouldContain("Gadget.cs");

        // Forward-slash relative path is what git emits and what content-at-ref keys on.
        client.ReadFileAtRef(first, "Gadget.cs").ShouldNotBeNull().ShouldContain("V1");
        client.ReadFileAtRef(second, "Gadget.cs").ShouldNotBeNull().ShouldContain("V2");
    }

    [Fact]
    public void content_at_a_ref_where_the_path_did_not_exist_is_null_not_an_error()
    {
        using var repo = new TempRepo();

        repo.Write("First.cs", "class First { }");
        repo.Git("add", "-A");
        repo.Commit("first");
        var first = repo.Head();

        var client = new GitClient(repo.Path);

        // Brand.cs never existed at the first commit — git show exits nonzero → null.
        client.ReadFileAtRef(first, "Brand.cs").ShouldBeNull();
    }

    [Fact]
    public void an_untracked_cs_file_is_reported_as_a_working_tree_change()
    {
        using var repo = new TempRepo();

        repo.Write("Tracked.cs", "class Tracked { }");
        repo.Git("add", "-A");
        repo.Commit("first");

        repo.Write("Untracked.cs", "class Untracked { }");

        var client = new GitClient(repo.Path);

        client.GetWorkingTreeChangedPaths().ShouldContain("Untracked.cs");
    }

    [Fact]
    public void working_tree_file_content_is_read_from_disk_and_null_when_absent()
    {
        using var repo = new TempRepo();

        repo.Write("OnDisk.cs", "class OnDisk { }");

        var client = new GitClient(repo.Path);

        client.ReadWorkingTreeFile("OnDisk.cs").ShouldNotBeNull().ShouldContain("OnDisk");
        client.ReadWorkingTreeFile("Missing.cs").ShouldBeNull();
    }

    [Fact]
    public void a_directory_that_is_not_a_repository_fails_repository_validation()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rt-not-a-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var client = new GitClient(dir);
            Should.Throw<InvalidOperationException>(() => client.EnsureRepository());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void an_unknown_ref_fails_ref_validation()
    {
        using var repo = new TempRepo();

        repo.Write("Gadget.cs", "class Gadget { }");
        repo.Git("add", "-A");
        repo.Commit("first");

        var client = new GitClient(repo.Path);

        client.EnsureRepository(); // sanity: it IS a repo
        Should.Throw<ArgumentException>(() => client.EnsureRefExists("no-such-ref"));
    }

    /// <summary>
    ///     A uniquely-named throwaway git repository under the temp dir. Configures a local
    ///     identity (commits fail without one) and disables signing / global hooks so a
    ///     developer's global config can't break the fixture. Cleaned up on dispose.
    /// </summary>
    private sealed class TempRepo : IDisposable
    {
        public TempRepo()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"rt-gitclient-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);

            Git("init", "-q");
            Git("config", "user.email", "rt-test@example.com");
            Git("config", "user.name", "rt test");
            Git("config", "commit.gpgsign", "false");
            Git("config", "core.hooksPath", "");
        }

        public string Path { get; }

        public void Write(string relativePath, string content)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            var dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(full, content);
        }

        public void Commit(string message) => Git("commit", "-q", "-m", message);

        public string Head() => Git("rev-parse", "HEAD").Trim();

        public string Git(params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = Path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("failed to start git");
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"git {string.Join(' ', args)} failed ({proc.ExitCode}): {stderr}");
            return stdout;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                /* best-effort cleanup; temp dir will be reclaimed by the OS */
            }
            catch (UnauthorizedAccessException)
            {
                /* a read-only .git object file can resist delete on Windows; ignore */
            }
        }
    }
}
