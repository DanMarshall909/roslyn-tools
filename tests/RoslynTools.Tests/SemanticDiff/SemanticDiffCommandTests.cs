using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RoslynTools;
using RoslynTools.Core;
using RoslynTools.Services.SemanticDiff;
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
    public async Task git_content_read_failure_is_a_typed_error_in_the_shared_operation_and_cli()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Broken.cs"],
            ReadFailure = new GitContentReadException(
                "HEAD", "Broken.cs", "git show HEAD:Broken.cs failed",
                new InvalidOperationException("object unavailable")),
        };
        var sc = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(sc);
        sc.Replace(ServiceDescriptor.Singleton<IGitClient>(git));
        using var services = sc.BuildServiceProvider();
        var result = await services.GetRequiredService<SemanticDiffOperation>()
            .ExecuteAsync(new SemanticDiffOperationRequest());

        var error = result.Match(
            _ => throw new ShouldAssertException("Expected Git read failure"),
            value => value);
        error.Kind.ShouldBe(RtErrorKind.ExternalToolFailure);
        error.Context.ShouldNotBeNull()["reference"].ShouldBe("HEAD");
        error.Context["path"].ShouldBe("Broken.cs");

        var (exit, stdout, stderr) = await InvokeAsync(
            ["analyze", "semantic-diff", "--json"], git);
        exit.ShouldBe(1);
        stderr.ShouldBeEmpty();
        using var document = JsonDocument.Parse(stdout);
        document.RootElement.GetProperty("kind").GetString().ShouldBe("ExternalToolFailure");
        document.RootElement.GetProperty("context").GetProperty("path").GetString()
            .ShouldBe("Broken.cs");
    }

    [Fact]
    public async Task unavailable_working_tree_mode_reports_a_typed_error_for_a_clean_tree()
    {
        var git = new RecordingGitClient { UnsupportedWorkingTree = true };

        var (exit, stdout, stderr) = await InvokeAsync(
            ["analyze", "semantic-diff", "--json"], git);

        exit.ShouldBe(1);
        stderr.ShouldBeEmpty();
        using var document = JsonDocument.Parse(stdout);
        document.RootElement.GetProperty("kind").GetString().ShouldBe("ExternalToolFailure");
        (document.RootElement.GetProperty("message").GetString() ?? string.Empty)
            .ShouldContain("unsupported", Case.Insensitive);
        var context = document.RootElement.GetProperty("context");
        context.GetProperty("reference").GetString().ShouldBe("WORKTREE");
        context.TryGetProperty("path", out _).ShouldBeFalse();
    }

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
        git.WorkingTreeBaseline.ShouldBe("v1");
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
    public async Task json_output_emits_an_array_of_deltas_with_fqn_and_status()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { void M() { var x = 1; } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { void M() { var x = 2; } }" },
        };

        var (exit, stdout, _) = await InvokeAsync(["analyze", "semantic-diff", "--json"], git);

        exit.ShouldBe(0);
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        var deltas = doc.RootElement.EnumerateArray().ToList();
        deltas.ShouldNotBeEmpty();
        var changed = deltas.ShouldHaveSingleItem();
        (changed.GetProperty("fqn").GetString() ?? "").ShouldContain("Gadget");
        changed.GetProperty("status").GetString().ShouldBe("Changed");
        // kind is the readable enum name, not its integer ordinal.
        changed.GetProperty("kind").GetString().ShouldBe("Method");
    }

    [Fact]
    public async Task json_output_with_no_semantic_changes_emits_an_empty_array()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { void M() { } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class    Gadget {\n   void M() {  }\n}" },
        };

        var (exit, stdout, _) = await InvokeAsync(["analyze", "semantic-diff", "--json"], git);

        exit.ShouldBe(0);
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task json_output_surfaces_a_relocated_declaration_with_both_paths()
    {
        // Same type, different file between the two refs: the engine reports Unchanged with a
        // path change (the text renderer prints "> Moved"). JSON must surface it too — the only
        // Unchanged delta that is not silent — so consumers can detect relocations.
        var git = new RecordingGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["Old.cs", "New.cs"] },
            FilesAtRef =
            {
                ["v1:Old.cs"] = "namespace Acme; class A { }",
                ["v2:New.cs"] = "namespace Acme; class A { }",
            },
        };

        var (exit, stdout, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "--from", "v1", "--to", "v2", "--json"], git);

        exit.ShouldBe(0);
        using var doc = JsonDocument.Parse(stdout);
        var moved = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("fqn").GetString() == "Acme.A");
        moved.GetProperty("status").GetString().ShouldBe("Unchanged");
        moved.GetProperty("oldPath").GetString().ShouldBe("Old.cs");
        moved.GetProperty("newPath").GetString().ShouldBe("New.cs");
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

    // ----- US-123: two files on disc -----

    [Fact]
    public async Task two_file_mode_compares_two_files_on_disc_and_writes_a_git_style_diff()
    {
        var files = new RecordingFileReader
        {
            Files =
            {
                ["old/Widget.cs"] = "class Widget { void M() { var x = 1; } }",
                ["new/Widget.cs"] = "class Widget { void M() { var x = 2; } }",
            },
        };
        // A git client that throws if touched — proves two-files mode never goes near git.
        var git = new RecordingGitClient { ThrowNotARepository = true };

        var (exit, stdout, stderr) = await InvokeAsync(
            ["analyze", "semantic-diff", "old/Widget.cs", "new/Widget.cs"], git, files);

        exit.ShouldBe(0);
        stdout.ShouldContain("Changed", Case.Sensitive);
        stdout.ShouldContain("Widget");
        stderr.ShouldBeEmpty();
    }

    [Fact]
    public async Task two_file_mode_works_outside_a_git_repository()
    {
        var files = new RecordingFileReader
        {
            Files =
            {
                ["a.cs"] = "class C { }",
                ["b.cs"] = "class C { }",
            },
        };
        // Not a git repository at all — two-files mode must still succeed.
        var git = new RecordingGitClient { ThrowNotARepository = true };

        var (exit, _, _) = await InvokeAsync(["analyze", "semantic-diff", "a.cs", "b.cs"], git, files);

        exit.ShouldBe(0);
        git.RefsValidated.ShouldBeEmpty();
        git.WorkingTreeRead.ShouldBeFalse();
    }

    [Fact]
    public async Task two_file_mode_emits_json_with_both_file_paths()
    {
        var files = new RecordingFileReader
        {
            Files =
            {
                ["old/Widget.cs"] = "class Widget { void M() { return; } }",
                ["new/Widget.cs"] = "class Widget { void M() { var x = 1; } }",
            },
        };
        var git = new RecordingGitClient { ThrowNotARepository = true };

        var (exit, stdout, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "old/Widget.cs", "new/Widget.cs", "--json"], git, files);

        exit.ShouldBe(0);
        using var doc = JsonDocument.Parse(stdout);
        var deltas = doc.RootElement.EnumerateArray().ToList();
        var changed = deltas.Single(d => d.GetProperty("status").GetString() == "Changed");
        changed.GetProperty("oldPath").GetString().ShouldBe("old/Widget.cs");
        changed.GetProperty("newPath").GetString().ShouldBe("new/Widget.cs");
    }

    [Fact]
    public async Task two_file_mode_does_not_report_unchanged_declarations_as_moved()
    {
        // The two files differ only in path (different names) and a comment. Every declaration
        // is semantically unchanged, so nothing should surface — no "> Moved" noise driven by
        // the inputs having different file names, and an empty JSON array.
        var files = new RecordingFileReader
        {
            Files =
            {
                ["old/Widget.cs"] = "class Widget { /* old */ int Score(int x) => x + 1; }",
                ["new/Widget.cs"] = "class Widget { int Score(int x) => x + 1; }",
            },
        };
        var git = new RecordingGitClient { ThrowNotARepository = true };

        var (textExit, textOut, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "old/Widget.cs", "new/Widget.cs"], git, files);
        textExit.ShouldBe(0);
        textOut.ShouldContain("no semantic changes");
        textOut.ShouldNotContain("Moved");

        var (jsonExit, jsonOut, _) = await InvokeAsync(
            ["analyze", "semantic-diff", "old/Widget.cs", "new/Widget.cs", "--json"], git, files);
        jsonExit.ShouldBe(0);
        using var doc = JsonDocument.Parse(jsonOut);
        doc.RootElement.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task a_missing_file_exits_one_with_a_clean_error()
    {
        var files = new RecordingFileReader { Files = { ["present.cs"] = "class C { }" } };
        var git = new RecordingGitClient { ThrowNotARepository = true };

        var (exit, stdout, stderr) = await InvokeAsync(
            ["analyze", "semantic-diff", "present.cs", "absent.cs"], git, files);

        exit.ShouldBe(1);
        stderr.ShouldContain("absent.cs");
        stderr.ShouldNotContain("at RoslynTools"); // no stack trace
        stdout.ShouldBeEmpty();
    }

    [Fact]
    public async Task a_single_file_argument_is_a_usage_error()
    {
        var git = new RecordingGitClient { ThrowNotARepository = true };

        var (exit, _, stderr) = await InvokeAsync(
            ["analyze", "semantic-diff", "only-one.cs"], git, new RecordingFileReader());

        exit.ShouldBe(1);
        stderr.ShouldContain("exactly two files");
        stderr.ShouldNotContain("at RoslynTools");
    }

    [Fact]
    public async Task file_arguments_combined_with_from_are_a_usage_error()
    {
        var files = new RecordingFileReader
        {
            Files = { ["a.cs"] = "class C { }", ["b.cs"] = "class C { }" },
        };
        var git = new RecordingGitClient();

        var (exit, _, stderr) = await InvokeAsync(
            ["analyze", "semantic-diff", "a.cs", "b.cs", "--from", "HEAD"], git, files);

        exit.ShouldBe(1);
        stderr.ShouldContain("cannot combine");
        stderr.ShouldNotContain("at RoslynTools");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> InvokeAsync(
        string[] args, IGitClient git, IFileTextReader? files = null)
    {
        var sc = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(sc);
        sc.Replace(ServiceDescriptor.Singleton(git));
        if (files is not null)
            sc.Replace(ServiceDescriptor.Singleton(files));
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
    ///     return null for an absent key or throw for an unreadable object.
    /// </summary>
    private sealed class RecordingGitClient : IGitClient
    {
        public bool ThrowNotARepository { get; init; }

        public bool UnsupportedWorkingTree { get; init; }

        public HashSet<string> InvalidRefs { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> WorkingTreeChangedPaths { get; init; } = [];

        public Dictionary<(string From, string To), IReadOnlyList<string>> ChangedPathsBetween { get; } = new();

        public Dictionary<string, string> FilesAtRef { get; } = new(StringComparer.Ordinal);

        public GitContentReadException? ReadFailure { get; init; }

        public Dictionary<string, string> WorkingTreeFiles { get; } = new(StringComparer.Ordinal);

        public List<string> RefsValidated { get; } = [];

        public List<(string From, string To)> ChangedPathsBetweenQueried { get; } = [];

        public bool WorkingTreeRead { get; private set; }

        public string? WorkingTreeBaseline { get; private set; }

        public void EnsureRepository()
        {
            if (ThrowNotARepository)
                throw new InvalidOperationException("not a git repository: <fake>");
        }

        public void EnsureWorkingTreeContentSupported()
        {
            if (UnsupportedWorkingTree)
                throw new GitContentReadException("WORKTREE", string.Empty,
                    "working-tree semantic diff is unsupported",
                    new PlatformNotSupportedException());
        }

        public void EnsureRefExists(string reference)
        {
            RefsValidated.Add(reference);
            if (InvalidRefs.Contains(reference))
                throw new ArgumentException($"invalid git ref: {reference}");
        }

        public string ResolveCommit(string reference)
        {
            EnsureRefExists(reference);
            return reference;
        }

        public IReadOnlyList<string> GetChangedPathsBetween(string fromRef, string toRef)
        {
            ChangedPathsBetweenQueried.Add((fromRef, toRef));
            return ChangedPathsBetween.TryGetValue((fromRef, toRef), out var paths) ? paths : [];
        }

        public IReadOnlyList<string> GetWorkingTreeChangedPaths(string baselineRef = "HEAD")
        {
            WorkingTreeBaseline = baselineRef;
            return WorkingTreeChangedPaths;
        }

        public string? ReadFileAtRef(string reference, string path)
        {
            if (ReadFailure is not null)
                throw ReadFailure;
            return FilesAtRef.TryGetValue($"{reference}:{path}", out var text) ? text : null;
        }

        public string? ReadWorkingTreeFile(string path)
        {
            WorkingTreeRead = true;
            return WorkingTreeFiles.TryGetValue(path, out var text) ? text : null;
        }
    }

    /// <summary>
    ///     In-memory <see cref="IFileTextReader" /> for the two-files-on-disc mode: returns the
    ///     seeded text for a known path and <c>null</c> for an unknown one (the missing-file case).
    /// </summary>
    private sealed class RecordingFileReader : IFileTextReader
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public string? TryReadAllText(string path) =>
            Files.TryGetValue(path, out var text) ? text : null;
    }
}
