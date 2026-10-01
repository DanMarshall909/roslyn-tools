using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RoslynTools.Services.SemanticDiff;
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
    public void changed_paths_between_refs_fail_when_a_ref_is_missing()
    {
        using var repo = new TempRepo();
        repo.Write("Gadget.cs", "class Gadget { }");
        repo.Git("add", "-A");
        repo.Commit("first");

        var error = Should.Throw<InvalidOperationException>(
            () => new GitClient(repo.Path).GetChangedPathsBetween("missing-ref", "HEAD"));

        error.Message.ShouldContain("diff", Case.Insensitive);
        error.Message.ShouldContain("missing-ref", Case.Sensitive);
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

        // Brand.cs never existed at the first commit; the tree lookup proves absence.
        client.ReadFileAtRef(first, "Brand.cs").ShouldBeNull();
    }

    [Fact]
    public void content_at_an_invalid_ref_is_a_failure_not_an_absent_file()
    {
        using var repo = new TempRepo();
        repo.Write("Present.cs", "class Present { }");
        repo.Git("add", "-A");
        repo.Commit("first");

        var error = Should.Throw<GitContentReadException>(
            () => new GitClient(repo.Path).ReadFileAtRef("missing-ref", "Present.cs"));

        error.Reference.ShouldBe("missing-ref");
        error.Path.ShouldBe("Present.cs");
        error.Message.ShouldContain("missing-ref", Case.Sensitive);
        error.Message.ShouldContain("Present.cs", Case.Sensitive);
    }

    [Fact]
    public void content_with_a_tree_entry_but_a_missing_blob_is_a_failure_not_an_absent_file()
    {
        using var repo = new TempRepo();
        repo.Git("config", "gc.auto", "0");
        repo.Write("Broken.cs", "class Broken { }");
        repo.Git("add", "-A");
        repo.Commit("first");
        var reference = repo.Head();
        var blob = repo.Git("rev-parse", $"{reference}:Broken.cs").Trim();
        var objectPath = System.IO.Path.Combine(repo.Path, ".git", "objects", blob[..2], blob[2..]);
        File.Exists(objectPath).ShouldBeTrue();
        File.Delete(objectPath);

        var error = Should.Throw<GitContentReadException>(
            () => new GitClient(repo.Path).ReadFileAtRef(reference, "Broken.cs"));

        error.Reference.ShouldBe(reference);
        error.Path.ShouldBe("Broken.cs");
        error.Message.ShouldContain(reference, Case.Sensitive);
        error.Message.ShouldContain("Broken.cs", Case.Sensitive);
    }

    [Fact]
    public void an_absent_literal_path_is_not_confused_with_a_similar_filename()
    {
        using var repo = new TempRepo();
        repo.Write("A1.cs", "class A1 { }");
        repo.Git("add", "-A");
        repo.Commit("first");

        new GitClient(repo.Path).ReadFileAtRef(repo.Head(), "A[1].cs").ShouldBeNull();
    }

    [Fact]
    public void a_present_path_with_pathspec_characters_returns_its_content()
    {
        using var repo = new TempRepo();
        repo.Write("A[1].cs", "class LiteralName { }");
        repo.Git("add", "-A");
        repo.Commit("first");

        new GitClient(repo.Path).ReadFileAtRef(repo.Head(), "A[1].cs")
            .ShouldBe("class LiteralName { }");
    }

    [Fact]
    public void source_provider_preserves_added_and_removed_files_from_a_nested_working_directory()
    {
        using var repo = new TempRepo();
        repo.Write("sub/Removed.cs", "class Removed { }");
        repo.Git("add", "-A");
        repo.Commit("first");
        var first = repo.Head();

        File.Delete(System.IO.Path.Combine(repo.Path, "sub", "Removed.cs"));
        repo.Write("sub/Added.cs", "class Added { }");
        repo.Git("add", "-A");
        repo.Commit("second");

        var provider = new GitSemanticDiffSourceProvider(
            new GitClient(System.IO.Path.Combine(repo.Path, "sub")));
        var (before, after) = provider.Load(first, repo.Head());

        before.Select(source => source.Path).ShouldBe(["sub/Removed.cs"]);
        after.Select(source => source.Path).ShouldBe(["sub/Added.cs"]);
    }

    [Fact]
    public void source_provider_reads_nested_working_tree_changes_from_the_repository_root()
    {
        using var repo = new TempRepo();
        repo.Write("sub/Changed.cs", "class Changed { int Value = 1; }");
        repo.Git("add", "-A");
        repo.Commit("first");

        repo.Write("sub/Changed.cs", "class Changed { int Value = 2; }");
        repo.Write("sub/Added.cs", "class Added { }");

        var provider = new GitSemanticDiffSourceProvider(
            new GitClient(System.IO.Path.Combine(repo.Path, "sub")));
        var (before, after) = provider.Load(null, null);

        before.Select(source => source.Path).ShouldBe(["sub/Changed.cs"]);
        after.Select(source => source.Path).ShouldBe(["sub/Changed.cs", "sub/Added.cs"]);
        after[0].Text.ShouldContain("Value = 2");
        after[1].Text.ShouldContain("class Added");
    }

    [Fact]
    public void changed_paths_preserve_literal_names_with_control_characters()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var repo = new TempRepo();
        var tracked = "Odd\tName.cs";
        var untracked = "New\nName.cs";
        repo.Write(tracked, "class Odd { int Value = 1; }");
        repo.Git("add", "-A");
        repo.Commit("first");

        repo.Write(tracked, "class Odd { int Value = 2; }");
        repo.Write(untracked, "class New { }");
        var client = new GitClient(repo.Path);

        client.GetWorkingTreeChangedPaths().ShouldBe([tracked, untracked]);
        var (before, after) = new GitSemanticDiffSourceProvider(client).Load(null, null);
        before.Select(source => source.Path).ShouldBe([tracked]);
        after.Select(source => source.Path).ShouldBe([tracked, untracked]);
    }

    [Fact]
    public void untracked_symlink_to_outside_content_fails_instead_of_reading_it()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var repo = new TempRepo();
        repo.Write("Present.cs", "class Present { }");
        repo.Git("add", "-A");
        repo.Commit("first");
        var outside = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"rt-outside-{Guid.NewGuid():N}.cs");
        File.WriteAllText(outside, "class PrivateOutsideContent { }");
        try
        {
            File.CreateSymbolicLink(System.IO.Path.Combine(repo.Path, "Leak.cs"), outside);
            var client = new GitClient(repo.Path);
            client.GetWorkingTreeChangedPaths().ShouldContain("Leak.cs");

            var error = Should.Throw<GitContentReadException>(
                () => new GitSemanticDiffSourceProvider(client).Load(null, null));
            error.Reference.ShouldBe("WORKTREE");
            error.Path.ShouldBe("Leak.cs");
            error.Message.ShouldNotContain("PrivateOutsideContent");
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void working_tree_read_rejects_parent_traversal_and_rooted_paths()
    {
        using var repo = new TempRepo();
        var client = new GitClient(repo.Path);

        Should.Throw<GitContentReadException>(() => client.ReadWorkingTreeFile("../Outside.cs"));
        Should.Throw<GitContentReadException>(() => client.ReadWorkingTreeFile(repo.Path));
    }

    [Fact]
    public void working_tree_read_rejects_a_parent_symlink_and_a_dangling_link()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var repo = new TempRepo();
        var outside = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"rt-outside-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        File.WriteAllText(System.IO.Path.Combine(outside, "Secret.cs"), "class Secret { }");
        try
        {
            Directory.CreateSymbolicLink(System.IO.Path.Combine(repo.Path, "Linked"), outside);
            File.CreateSymbolicLink(System.IO.Path.Combine(repo.Path, "Dangling.cs"),
                System.IO.Path.Combine(outside, "Absent.cs"));
            var client = new GitClient(repo.Path);

            var parentError = Should.Throw<GitContentReadException>(
                () => client.ReadWorkingTreeFile("Linked/Secret.cs"));
            parentError.Path.ShouldBe("Linked/Secret.cs");
            Should.Throw<GitContentReadException>(
                () => client.ReadWorkingTreeFile("Dangling.cs"));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void a_deleted_tracked_working_tree_file_is_a_proven_absence()
    {
        using var repo = new TempRepo();
        repo.Write("Deleted.cs", "class Deleted { }");
        repo.Git("add", "-A");
        repo.Commit("first");
        File.Delete(System.IO.Path.Combine(repo.Path, "Deleted.cs"));

        var (before, after) = new GitSemanticDiffSourceProvider(new GitClient(repo.Path))
            .Load(null, null);

        before.Select(source => source.Path).ShouldBe(["Deleted.cs"]);
        after.ShouldBeEmpty();
    }

    [Fact]
    public void an_unreadable_working_tree_file_fails_instead_of_appearing_deleted()
    {
        if (OperatingSystem.IsWindows() || string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
            return;

        using var repo = new TempRepo();
        repo.Write("Restricted.cs", "class Restricted { int Value = 1; }");
        repo.Git("add", "-A");
        repo.Commit("first");
        repo.Write("Restricted.cs", "class Restricted { int Value = 2; }");
        var file = System.IO.Path.Combine(repo.Path, "Restricted.cs");
        var originalMode = File.GetUnixFileMode(file);
        try
        {
            File.SetUnixFileMode(file, UnixFileMode.None);
            var error = Should.Throw<GitContentReadException>(
                () => new GitSemanticDiffSourceProvider(new GitClient(repo.Path)).Load(null, null));
            error.Reference.ShouldBe("WORKTREE");
            error.Path.ShouldBe("Restricted.cs");
        }
        finally
        {
            File.SetUnixFileMode(file, originalMode);
        }
    }

    [Fact]
    public async Task a_tracked_file_replaced_by_a_fifo_fails_without_blocking_the_reader()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var repo = new TempRepo();
        repo.Write("Pipe.cs", "class Pipe { }");
        repo.Git("add", "-A");
        repo.Commit("first");
        var file = System.IO.Path.Combine(repo.Path, "Pipe.cs");
        File.Delete(file);
        var start = new ProcessStartInfo("mkfifo")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(file);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start mkfifo");
        process.WaitForExit();
        process.ExitCode.ShouldBe(0);

        var client = new GitClient(repo.Path);
        client.GetWorkingTreeChangedPaths().ShouldContain("Pipe.cs");
        var read = Task.Run(() => client.ReadWorkingTreeFile("Pipe.cs"));
        var error = await Should.ThrowAsync<GitContentReadException>(
            async () => await read.WaitAsync(TimeSpan.FromSeconds(2)));
        error.Path.ShouldBe("Pipe.cs");
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
    public void working_tree_changes_fail_when_head_does_not_exist()
    {
        using var repo = new TempRepo();
        repo.Write("Untracked.cs", "class Untracked { }");

        var error = Should.Throw<InvalidOperationException>(
            () => new GitClient(repo.Path).GetWorkingTreeChangedPaths());

        error.Message.ShouldContain("HEAD", Case.Sensitive);
    }

    [Fact]
    public void working_tree_changes_report_a_git_process_start_failure()
    {
        var missingDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"rt-git-missing-{Guid.NewGuid():N}");

        var error = Should.Throw<InvalidOperationException>(
            () => new GitClient(missingDirectory).GetWorkingTreeChangedPaths());

        error.Message.ShouldContain("git", Case.Insensitive);
        error.Message.ShouldContain(missingDirectory, Case.Sensitive);
    }

    [Fact]
    public void changed_path_failure_drains_large_git_diagnostics()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"rt-git-stderr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var executable = System.IO.Path.Combine(directory, "fake-git");
            File.WriteAllText(executable, """
                #!/bin/sh
                ( sleep 10; kill -TERM "$$" 2>/dev/null ) >/dev/null 2>&1 &
                watchdog=$!
                i=0
                while [ "$i" -lt 8192 ]; do
                    printf '0123456789abcdef0123456789abcdef\n' >&2
                    i=$((i + 1))
                done
                kill "$watchdog" 2>/dev/null
                exit 9
                """);
            File.SetUnixFileMode(executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var error = Should.Throw<InvalidOperationException>(
                () => new GitClient(directory, executable).GetWorkingTreeChangedPaths());

            error.Message.ShouldContain("exit 9", Case.Sensitive);
            error.Message.Length.ShouldBeLessThan(1024);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
