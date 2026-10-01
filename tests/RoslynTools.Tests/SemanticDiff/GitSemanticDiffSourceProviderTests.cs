using RoslynTools.Services.SemanticDiff;
using RoslynTools.Services.SemanticDiff.Git;
using Shouldly;

namespace RoslynTools.Tests.SemanticDiff;

/// <summary>
///     Tests for <see cref="GitSemanticDiffSourceProvider" /> — the git version source
///     that feeds the semantic-diff engine. Driven entirely by a <see cref="FakeGitClient" />
///     so no real repository is touched (US-122 batch 3). The provider's job is path
///     selection (.cs filter), side membership (the union), and the two modes
///     (working-tree-vs-baseline and ref-vs-ref); the engine itself is tested separately.
/// </summary>
public sealed class GitSemanticDiffSourceProviderTests
{
    [Fact]
    public void default_mode_loads_head_on_the_before_side_and_working_tree_on_the_after_side()
    {
        var git = new FakeGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef =
            {
                ["HEAD:Gadget.cs"] = "class Gadget { void Old() { } }",
            },
            WorkingTreeFiles =
            {
                ["Gadget.cs"] = "class Gadget { void New() { } }",
            },
        };
        var provider = new GitSemanticDiffSourceProvider(git);

        var (before, after) = provider.Load(fromRef: null, toRef: null);

        before.ShouldHaveSingleItem().Text.ShouldContain("Old");
        after.ShouldHaveSingleItem().Text.ShouldContain("New");
    }

    [Fact]
    public void an_untracked_new_cs_file_appears_only_on_the_after_side()
    {
        var git = new FakeGitClient
        {
            // git ls-files --others surfaces it as changed; it has no HEAD content.
            WorkingTreeChangedPaths = ["Brand.cs"],
            WorkingTreeFiles =
            {
                ["Brand.cs"] = "class Brand { }",
            },
        };
        var provider = new GitSemanticDiffSourceProvider(git);

        var (before, after) = provider.Load(fromRef: null, toRef: null);

        before.ShouldBeEmpty();
        after.ShouldHaveSingleItem().Path.ShouldBe("Brand.cs");
    }

    [Fact]
    public void ref_vs_ref_mode_loads_from_ref_content_before_and_to_ref_content_after()
    {
        var git = new FakeGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["Gadget.cs"] },
            FilesAtRef =
            {
                ["v1:Gadget.cs"] = "class Gadget { void V1() { } }",
                ["v2:Gadget.cs"] = "class Gadget { void V2() { } }",
            },
        };
        var provider = new GitSemanticDiffSourceProvider(git);

        var (before, after) = provider.Load(fromRef: "v1", toRef: "v2");

        before.ShouldHaveSingleItem().Text.ShouldContain("V1");
        after.ShouldHaveSingleItem().Text.ShouldContain("V2");
    }

    [Fact]
    public void moving_ref_names_are_pinned_before_ref_path_discovery_and_content_reads()
    {
        var git = new FakeGitClient
        {
            ResolvedCommits = { ["moving"] = "oid-before", ["latest"] = "oid-after" },
            ChangedPathsBetween = { [("oid-before", "oid-after")] = ["Gadget.cs"] },
            FilesAtRef =
            {
                ["oid-before:Gadget.cs"] = "class Gadget { void Before() { } }",
                ["oid-after:Gadget.cs"] = "class Gadget { void After() { } }",
            },
        };

        var (before, after) = new GitSemanticDiffSourceProvider(git).Load("moving", "latest");

        before.ShouldHaveSingleItem().Text.ShouldContain("Before");
        after.ShouldHaveSingleItem().Text.ShouldContain("After");
    }

    [Fact]
    public void moving_baseline_is_pinned_for_working_tree_paths_and_before_content()
    {
        var git = new FakeGitClient
        {
            ResolvedCommits = { ["moving"] = "oid-before" },
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["oid-before:Gadget.cs"] = "class Gadget { void Before() { } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { void After() { } }" },
        };

        var (before, after) = new GitSemanticDiffSourceProvider(git).Load("moving", null);

        git.WorkingTreeBaseline.ShouldBe("oid-before");
        before.ShouldHaveSingleItem().Text.ShouldContain("Before");
        after.ShouldHaveSingleItem().Text.ShouldContain("After");
    }

    [Fact]
    public void non_cs_changed_paths_are_filtered_out_of_both_sides()
    {
        var git = new FakeGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["readme.md", "Gadget.cs", "app.config"] },
            FilesAtRef =
            {
                ["v1:Gadget.cs"] = "class Gadget { }",
                ["v2:Gadget.cs"] = "class Gadget { }",
                ["v1:readme.md"] = "# old",
                ["v2:readme.md"] = "# new",
                ["v1:app.config"] = "<old/>",
                ["v2:app.config"] = "<new/>",
            },
        };
        var provider = new GitSemanticDiffSourceProvider(git);

        var (before, after) = provider.Load(fromRef: "v1", toRef: "v2");

        before.ShouldHaveSingleItem().Path.ShouldBe("Gadget.cs");
        after.ShouldHaveSingleItem().Path.ShouldBe("Gadget.cs");
    }

    [Fact]
    public void cs_filter_is_case_insensitive_so_uppercase_extension_is_included()
    {
        var git = new FakeGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["Gadget.CS"] },
            FilesAtRef =
            {
                ["v1:Gadget.CS"] = "class Gadget { }",
                ["v2:Gadget.CS"] = "class Gadget { }",
            },
        };
        var provider = new GitSemanticDiffSourceProvider(git);

        var (before, after) = provider.Load(fromRef: "v1", toRef: "v2");

        before.ShouldHaveSingleItem().Path.ShouldBe("Gadget.CS");
        after.ShouldHaveSingleItem().Path.ShouldBe("Gadget.CS");
    }

    [Fact]
    public void a_path_present_on_only_the_from_side_is_included_on_the_before_side_only()
    {
        var git = new FakeGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["Removed.cs"] },
            FilesAtRef =
            {
                ["v1:Removed.cs"] = "class Removed { }",
                // not present at v2 — git show returns null
            },
        };
        var provider = new GitSemanticDiffSourceProvider(git);

        var (before, after) = provider.Load(fromRef: "v1", toRef: "v2");

        before.ShouldHaveSingleItem().Path.ShouldBe("Removed.cs");
        after.ShouldBeEmpty();
    }

    [Fact]
    public void a_path_present_on_only_the_to_side_is_included_on_the_after_side_only()
    {
        var git = new FakeGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["Added.cs"] },
            FilesAtRef =
            {
                // not present at v1 — git show returns null
                ["v2:Added.cs"] = "class Added { }",
            },
        };
        var provider = new GitSemanticDiffSourceProvider(git);

        var (before, after) = provider.Load(fromRef: "v1", toRef: "v2");

        before.ShouldBeEmpty();
        after.ShouldHaveSingleItem().Path.ShouldBe("Added.cs");
    }

    [Fact]
    public void a_class_moved_to_a_new_file_feeds_the_old_path_before_and_the_new_path_after()
    {
        // --no-renames lists BOTH paths; each is fed on the side it exists, so the engine
        // can match them by FQN and report the type as Unchanged.
        var git = new FakeGitClient
        {
            ChangedPathsBetween = { [("v1", "v2")] = ["A.cs", "B.cs"] },
            FilesAtRef =
            {
                ["v1:A.cs"] = "class Mover { void M() { } }",
                // A.cs deleted at v2 → null; B.cs new at v2.
                ["v2:B.cs"] = "class Mover { void M() { } }",
            },
        };
        var provider = new GitSemanticDiffSourceProvider(git);

        var (before, after) = provider.Load(fromRef: "v1", toRef: "v2");

        before.ShouldHaveSingleItem().Path.ShouldBe("A.cs");
        after.ShouldHaveSingleItem().Path.ShouldBe("B.cs");
    }

    [Fact]
    public void not_a_git_repository_surfaces_as_invalid_operation()
    {
        var git = new FakeGitClient { ThrowNotARepository = true };
        var provider = new GitSemanticDiffSourceProvider(git);

        Should.Throw<InvalidOperationException>(() => provider.Load(fromRef: null, toRef: null));
    }

    [Fact]
    public void an_invalid_ref_surfaces_as_an_argument_error()
    {
        var git = new FakeGitClient { InvalidRefs = { "nope" } };
        var provider = new GitSemanticDiffSourceProvider(git);

        Should.Throw<ArgumentException>(() => provider.Load(fromRef: "nope", toRef: "HEAD"));
    }

    /// <summary>
    ///     In-memory <see cref="IGitClient" /> with the same two error channels as the real
    ///     client: validation throws, content reads return null for an absent key.
    /// </summary>
    private sealed class FakeGitClient : IGitClient
    {
        public bool ThrowNotARepository { get; init; }

        public HashSet<string> InvalidRefs { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> WorkingTreeChangedPaths { get; init; } = [];

        public Dictionary<(string From, string To), IReadOnlyList<string>> ChangedPathsBetween { get; } = new();

        public Dictionary<string, string> FilesAtRef { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> ResolvedCommits { get; } = new(StringComparer.Ordinal);

        public string? WorkingTreeBaseline { get; private set; }

        public Dictionary<string, string> WorkingTreeFiles { get; } = new(StringComparer.Ordinal);

        public void EnsureRepository()
        {
            if (ThrowNotARepository)
                throw new InvalidOperationException("not a git repository: <fake>");
        }

        public void EnsureWorkingTreeContentSupported()
        {
        }

        public void EnsureRefExists(string reference)
        {
            if (InvalidRefs.Contains(reference))
                throw new ArgumentException($"invalid git ref: {reference}");
        }

        public string ResolveCommit(string reference)
        {
            EnsureRefExists(reference);
            return ResolvedCommits.TryGetValue(reference, out var commit) ? commit : reference;
        }

        public IReadOnlyList<string> GetChangedPathsBetween(string fromRef, string toRef) =>
            ChangedPathsBetween.TryGetValue((fromRef, toRef), out var paths) ? paths : [];

        public IReadOnlyList<string> GetWorkingTreeChangedPaths(string baselineRef = "HEAD")
        {
            WorkingTreeBaseline = baselineRef;
            return WorkingTreeChangedPaths;
        }

        public string? ReadFileAtRef(string reference, string path) =>
            FilesAtRef.TryGetValue($"{reference}:{path}", out var text) ? text : null;

        public string? ReadWorkingTreeFile(string path) =>
            WorkingTreeFiles.TryGetValue(path, out var text) ? text : null;
    }
}
