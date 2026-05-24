using RoslynTools.Rendering;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public class CSharpHighlighter
{
    [Fact]
    public void wraps_keywords_in_blue_markup()
    {
        var result = RoslynTools.Rendering.CSharpHighlighter.ToMarkup("public void Foo()");

        result.ShouldContain("[blue]public[/]");
        result.ShouldContain("[blue]void[/]");
    }

    [Fact]
    public void wraps_string_literals_in_dark_orange_markup()
    {
        var result = RoslynTools.Rendering.CSharpHighlighter.ToMarkup("var x = \"hello\";");

        result.ShouldContain("[darkorange3]\"hello\"[/]");
    }

    [Fact]
    public void wraps_numeric_literals_in_cyan_markup()
    {
        var result = RoslynTools.Rendering.CSharpHighlighter.ToMarkup("var count = 42;");

        result.ShouldContain("[cyan]42[/]");
    }

    [Fact]
    public void wraps_single_line_comments_in_grey_markup()
    {
        var result = RoslynTools.Rendering.CSharpHighlighter.ToMarkup("// a comment");

        result.ShouldContain("[grey]// a comment[/]");
    }

    [Fact]
    public void leaves_identifiers_and_punctuation_uncoloured()
    {
        var result = RoslynTools.Rendering.CSharpHighlighter.ToMarkup("myVar = 1;");

        // The identifier 'myVar' is not wrapped in any colour markup tag.
        result.ShouldNotContain("[blue]myVar[/]");
        result.ShouldNotContain("[darkorange3]myVar[/]");
        // But numeric literal IS coloured.
        result.ShouldContain("[cyan]1[/]");
    }

    [Fact]
    public void returns_empty_for_empty_input()
    {
        var result = RoslynTools.Rendering.CSharpHighlighter.ToMarkup(string.Empty);

        result.ShouldBe(string.Empty);
    }

    [Fact]
    public void escapes_literal_brackets_in_identifiers_so_markup_renders_safely()
    {
        // Attribute syntax — the brackets must be escaped to avoid Spectre parsing them as tags.
        var result = RoslynTools.Rendering.CSharpHighlighter.ToMarkup("[Theory]");

        result.ShouldContain("[[Theory]]");
    }
}
