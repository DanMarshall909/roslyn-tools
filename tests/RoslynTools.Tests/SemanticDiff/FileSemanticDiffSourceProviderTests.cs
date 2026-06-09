using RoslynTools.Services.SemanticDiff;
using Shouldly;

namespace RoslynTools.Tests.SemanticDiff;

/// <summary>
///     Tests for <see cref="FileSemanticDiffSourceProvider" /> — the two-files-on-disc source
///     for the semantic diff. The provider is constructed directly with a fake
///     <see cref="IFileTextReader" />: no real files, no git, no MSBuild.
/// </summary>
public sealed class FileSemanticDiffSourceProviderTests
{
    [Fact]
    public void loads_the_left_file_as_before_and_the_right_file_as_after()
    {
        var reader = new FakeFileTextReader
        {
            Files =
            {
                ["old.cs"] = "class C { }",
                ["new.cs"] = "class C { void M() { } }",
            },
        };
        var provider = new FileSemanticDiffSourceProvider(reader);

        var (before, after) = provider.Load("old.cs", "new.cs");

        before.Count.ShouldBe(1);
        before[0].Path.ShouldBe("old.cs");
        before[0].Text.ShouldBe("class C { }");
        after.Count.ShouldBe(1);
        after[0].Path.ShouldBe("new.cs");
        after[0].Text.ShouldBe("class C { void M() { } }");
    }

    [Fact]
    public void a_missing_left_file_throws_io_exception_naming_the_path()
    {
        var reader = new FakeFileTextReader { Files = { ["right.cs"] = "class C { }" } };
        var provider = new FileSemanticDiffSourceProvider(reader);

        var ex = Should.Throw<IOException>(() => provider.Load("missing-left.cs", "right.cs"));
        ex.Message.ShouldContain("missing-left.cs");
    }

    [Fact]
    public void a_missing_right_file_throws_io_exception_naming_the_path()
    {
        var reader = new FakeFileTextReader { Files = { ["left.cs"] = "class C { }" } };
        var provider = new FileSemanticDiffSourceProvider(reader);

        var ex = Should.Throw<IOException>(() => provider.Load("left.cs", "missing-right.cs"));
        ex.Message.ShouldContain("missing-right.cs");
    }

    [Fact]
    public void a_null_reader_is_rejected_at_construction()
    {
        Should.Throw<ArgumentNullException>(() => new FileSemanticDiffSourceProvider(null!));
    }

    private sealed class FakeFileTextReader : IFileTextReader
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public string? TryReadAllText(string path) =>
            Files.TryGetValue(path, out var text) ? text : null;
    }
}
