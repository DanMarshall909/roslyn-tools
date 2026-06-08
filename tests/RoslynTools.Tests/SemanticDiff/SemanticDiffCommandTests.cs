using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RoslynTools;
using RoslynTools.Services.SemanticDiff.Git;
using Shouldly;

namespace RoslynTools.Tests.SemanticDiff;

/// <summary>
///     Tests for the <c>rt analyze semantic-diff</c> CLI command — the thin host adapter
///     that wires the git version source into the engine and the git-diff text renderer
///     (US-122 batches 5+6). Each test drives the command end-to-end through
///     <see cref="App.RunAsync(string[], IServiceProvider?)" /> against the real DI graph,
///     with only <see cref="IGitClient" /> replaced by a <see cref="RecordingGitClient" />
///     so no real repository is touched and the before/after snapshots are deterministic.
/// </summary>
public sealed class SemanticDiffCommandTests
{
    [Fact]
    public async Task semantic_diff_with_a_changed_method_writes_a_git_style_diff_to_stdout()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { void M() { var x = 1; } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { void M() { var x = 2; } }" },
        };

        var (exit, stdout, stderr) = await InvokeAsync(["analyze", "semantic-diff"], git);

        exit.ShouldBe(0);
        stdout.ShouldContain("Changed", Case.Sensitive);
        stdout.ShouldContain("Gadget");
        stderr.ShouldNotContain("Changed");
    }

    [Fact]
    public async Task semantic_diff_with_no_semantic_changes_reports_the_terse_no_changes_line()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            // Only whitespace differs — semantically identical, so nothing is emitted.
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { void M() { } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class    Gadget {\n   void M() {  }\n}" },
        };

        var (exit, stdout, _) = await InvokeAsync(["analyze", "semantic-diff"], git);

        exit.ShouldBe(0);
        stdout.ShouldContain("no semantic changes");
    }

    [Fact]
    public async Task default_mode_loads_the_working_tree_against_head()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { }" },
        };

        var (exit, _, _) = await InvokeAsync(["analyze", "semantic-diff"], git);

        exit.ShouldBe(0);
        // Neither --from nor --to: provider.Load(null, null) => from defaults to HEAD,
        // working tree is the after side. No ref-vs-ref diff is requested.
        git.RefsValidated.ShouldContain("HEAD");
        git.WorkingTreeRead.ShouldBeTrue();
        git.ChangedPathsBetweenQueried.ShouldBeEmpty();
    }

    [Fact]
    public async Task from_and_to_refs_are_passed_straight_through_to_the_provider()
    {
        var git = new RecordingGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["Gadget.cs"] },
            FilesAtRef =
            {
                ["v1:Gadget.cs"] = "class Gadget { }",
                ["v2:Gadget.cs"] = "class Gadget { }",
            },
        };

        var (exit, _, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "--from", "v1", "--to", "v2"], git);

        exit.ShouldBe(0);
        git.RefsValidated.ShouldContain("v1");
        git.RefsValidated.ShouldContain("v2");
        git.ChangedPathsBetweenQueried.ShouldContain(("v1", "v2"));
    }

    [Fact]
    public async Task from_ref_alone_compares_that_ref_against_the_working_tree()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["v1:Gadget.cs"] = "class Gadget { }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { }" },
        };

        var (exit, _, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "--from", "v1"], git);

        exit.ShouldBe(0);
        // --from without --to: working-tree-vs-ref. from is v1, after is the working tree.
        git.RefsValidated.ShouldContain("v1");
        git.WorkingTreeRead.ShouldBeTrue();
        git.ChangedPathsBetweenQueried.ShouldBeEmpty();
    }

    [Fact]
    public async Task ignore_accessibility_treats_an_accessibility_only_change_as_unchanged()
    {
        var git = new RecordingGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["Gadget.cs"] },
            FilesAtRef =
            {
                ["v1:Gadget.cs"] = "public class Gadget { }",
                ["v2:Gadget.cs"] = "internal class Gadget { }",
            },
        };

        var (withoutFlag, withoutOut, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "--from", "v1", "--to", "v2"], git);
        var (withFlag, withOut, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "--from", "v1", "--to", "v2", "--ignore-accessibility"], git);

        withoutFlag.ShouldBe(0);
        withFlag.ShouldBe(0);
        // Default: accessibility differences register as Changed.
        withoutOut.ShouldContain("Changed", Case.Sensitive);
        // With the flag: the only difference is accessibility, so nothing is emitted.
        withOut.ShouldContain("no semantic changes");
    }

    [Fact]
    public async Task no_detect_renames_reports_a_pure_rename_as_an_add_and_a_remove()
    {
        var git = new RecordingGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["Gadget.cs"] },
            FilesAtRef =
            {
                ["v1:Gadget.cs"] = "class Gadget { void Old() { } }",
                ["v2:Gadget.cs"] = "class Gadget { void New() { } }",
            },
        };

        var (withDetect, detectOut, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "--from", "v1", "--to", "v2"], git);
        var (withoutDetect, plainOut, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "--from", "v1", "--to", "v2", "--no-detect-renames"], git);

        withDetect.ShouldBe(0);
        withoutDetect.ShouldBe(0);
        // Default: rename detection on, so the method pairs into a single Renamed delta.
        detectOut.ShouldContain("Renamed", Case.Sensitive);
        // Flag off: the rename collapses back to an independent Added + Removed pair.
        plainOut.ShouldContain("Added", Case.Sensitive);
        plainOut.ShouldContain("Removed", Case.Sensitive);
        plainOut.ShouldNotContain("Renamed");
    }

    [Fact]
    public async Task not_a_git_repository_exits_one_with_a_clean_stderr_message()
    {
        var git = new RecordingGitClient { ThrowNotARepository = true };

        var (exit, stdout, stderr) = await InvokeAsync(["analyze", "semantic-diff"], git);

        exit.ShouldBe(1);
        stderr.ShouldContain("not a git repository");
        stderr.ShouldNotContain("at RoslynTools"); // no stack trace
        stdout.ShouldBeEmpty();
    }

    [Fact]
    public async Task an_invalid_ref_exits_one_with_a_clean_stderr_message()
    {
        var git = new RecordingGitClient { InvalidRefs = { "nope" } };

        var (exit, _, stderr) = await InvokeAsync(
            ["analyze", "semantic-diff", "--from", "nope", "--to", "HEAD"], git);

        exit.ShouldBe(1);
        stderr.ShouldContain("invalid git ref");
        stderr.ShouldNotContain("at RoslynTools"); // no stack trace
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> InvokeAsync(
        string[] args, IGitClient git)
    {
        var sc = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(sc);
        sc.Replace(ServiceDescriptor.Singleton(git));
        using var services = sc.BuildServiceProvider();

        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exitCode = await App.RunAsync(args, services);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     In-memory <see cref="IGitClient" /> that records which refs were validated, whether
    ///     the working tree was read, and which ref-pairs were diffed — so a command test can
    ///     assert that <c>--from</c>/<c>--to</c> map straight through to the provider's Load.
    ///     Mirrors the real client's two error channels: validation throws, content reads
    ///     return null for an absent key.
    /// </summary>
    private sealed class RecordingGitClient : IGitClient
    {
        public bool ThrowNotARepository { get; init; }

        public HashSet<string> InvalidRefs { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> WorkingTreeChangedPaths { get; init; } = [];

        public Dictionary<(string From, string To), IReadOnlyList<string>> ChangedPathsBetween { get; } = new();

        public Dictionary<string, string> FilesAtRef { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> WorkingTreeFiles { get; } = new(StringComparer.Ordinal);

        public List<string> RefsValidated { get; } = [];

        public List<(string From, string To)> ChangedPathsBetweenQueried { get; } = [];

        public bool WorkingTreeRead { get; private set; }

        public void EnsureRepository()
        {
            if (ThrowNotARepository)
                throw new InvalidOperationException("not a git repository: <fake>");
        }

        public void EnsureRefExists(string reference)
        {
            RefsValidated.Add(reference);
            if (InvalidRefs.Contains(reference))
                throw new ArgumentException($"invalid git ref: {reference}");
        }

        public IReadOnlyList<string> GetChangedPathsBetween(string fromRef, string toRef)
        {
            ChangedPathsBetweenQueried.Add((fromRef, toRef));
            return ChangedPathsBetween.TryGetValue((fromRef, toRef), out var paths) ? paths : [];
        }

        public IReadOnlyList<string> GetWorkingTreeChangedPaths() => WorkingTreeChangedPaths;

        public string? ReadFileAtRef(string reference, string path) =>
            FilesAtRef.TryGetValue($"{reference}:{path}", out var text) ? text : null;

        public string? ReadWorkingTreeFile(string path)
        {
            WorkingTreeRead = true;
            return WorkingTreeFiles.TryGetValue(path, out var text) ? text : null;
        }
    }
}
