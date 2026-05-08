using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

public class RtIgnore
{
    private static Core.RtIgnore LoadFromLines(params string[] lines)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, ".rtignore"), lines);
        return Core.RtIgnore.Load(dir);
    }

    [Fact]
    public void returns_empty_when_no_file_exists()
    {
        var ignore = Core.RtIgnore.Load(Path.GetTempPath() + Path.GetRandomFileName());
        Assert.False(ignore.IsIgnored(@"C:/code/src/Foo.cs"));
    }

    [Fact]
    public void ignores_null_path()
    {
        var ignore = LoadFromLines("obj");
        Assert.False(ignore.IsIgnored(null));
    }

    [Fact]
    public void plain_segment_matches_folder_in_path()
    {
        var ignore = LoadFromLines("obj");
        Assert.True(ignore.IsIgnored(@"C:/code/src/obj/Debug/Foo.cs"));
        Assert.False(ignore.IsIgnored(@"C:/code/src/MyObj/Foo.cs"));
    }

    [Fact]
    public void directory_pattern_with_slash_matches_path_segment()
    {
        var ignore = LoadFromLines("src/Migrations/");
        Assert.True(ignore.IsIgnored(@"C:/code/src/Migrations/20240101_Init.cs"));
        Assert.False(ignore.IsIgnored(@"C:/code/src/Models/Foo.cs"));
    }

    [Fact]
    public void wildcard_extension_matches_filename()
    {
        var ignore = LoadFromLines("*.generated.cs");
        Assert.True(ignore.IsIgnored(@"C:/code/src/Foo.generated.cs"));
        Assert.False(ignore.IsIgnored(@"C:/code/src/Foo.cs"));
    }

    [Fact]
    public void comment_lines_are_skipped()
    {
        var ignore = LoadFromLines("# this is a comment", "obj");
        Assert.True(ignore.IsIgnored(@"C:/code/src/obj/Foo.cs"));
    }

    [Fact]
    public void blank_lines_are_skipped()
    {
        var ignore = LoadFromLines("", "   ", "obj");
        Assert.True(ignore.IsIgnored(@"C:/code/src/obj/Foo.cs"));
    }

    [Fact]
    public void case_insensitive_matching()
    {
        var ignore = LoadFromLines("Migrations");
        Assert.True(ignore.IsIgnored(@"C:/code/src/migrations/Foo.cs"));
        Assert.True(ignore.IsIgnored(@"C:/code/src/MIGRATIONS/Foo.cs"));
    }

    [Fact]
    public void absolute_folder_path_pattern()
    {
        var ignore = LoadFromLines("src/RoslynTools/Commands/");
        Assert.True(ignore.IsIgnored(@"C:/code/roslyn-tools/src/RoslynTools/Commands/FooCommand.cs"));
        Assert.False(ignore.IsIgnored(@"C:/code/roslyn-tools/src/RoslynTools/Core/FooCore.cs"));
    }

    [Fact]
    public void applies_command_scoped_pattern_only_to_that_command()
    {
        var ignore = LoadFromLines("list-imports:Generated/");

        Assert.True(ignore.IsIgnored(@"C:/code/src/Generated/Foo.cs", "list-imports"));
        Assert.False(ignore.IsIgnored(@"C:/code/src/Generated/Foo.cs", "find-unused"));
        Assert.False(ignore.IsIgnored(@"C:/code/src/Generated/Foo.cs"));
    }

    [Fact]
    public void supports_command_wildcards_in_command_scoped_pattern()
    {
        var ignore = LoadFromLines("find-*:Generated/");

        Assert.True(ignore.IsIgnored(@"C:/code/src/Generated/Foo.cs", "find-unused"));
        Assert.True(ignore.IsIgnored(@"C:/code/src/Generated/Foo.cs", "find-duplicates"));
        Assert.False(ignore.IsIgnored(@"C:/code/src/Generated/Foo.cs", "list-imports"));
    }

    [Fact]
    public void unscoped_pattern_still_applies_to_all_commands()
    {
        var ignore = LoadFromLines("Generated/");

        Assert.True(ignore.IsIgnored(@"C:/code/src/Generated/Foo.cs", "list-imports"));
        Assert.True(ignore.IsIgnored(@"C:/code/src/Generated/Foo.cs", "find-unused"));
        Assert.True(ignore.IsIgnored(@"C:/code/src/Generated/Foo.cs"));
    }
}
