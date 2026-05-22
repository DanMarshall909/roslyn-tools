using FPF = RoslynTools.Core.PreviewFormatter;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class PreviewFormatter_TruncatePreviewLine
{
    [Fact]
    public void returns_short_single_line_unchanged() =>
        FPF.TruncatePreviewLine("int x = 1;").ShouldBe("int x = 1;");

    [Fact]
    public void returns_exactly_120_chars_unchanged()
    {
        var input = new string('a', 120);
        FPF.TruncatePreviewLine(input).ShouldBe(input);
    }

    [Fact]
    public void truncates_121_char_line_to_120_plus_ellipsis()
    {
        var input = new string('a', 121);
        FPF.TruncatePreviewLine(input).ShouldBe(new string('a', 120) + " …");
    }

    [Fact]
    public void takes_first_line_on_lf_input() =>
        FPF.TruncatePreviewLine("first\nsecond").ShouldBe("first …");

    [Fact]
    public void takes_first_line_on_crlf_input() =>
        FPF.TruncatePreviewLine("first\r\nsecond").ShouldBe("first …");

    [Fact]
    public void takes_first_line_on_cr_input() =>
        FPF.TruncatePreviewLine("first\rsecond").ShouldBe("first …");

    [Fact]
    public void trims_trailing_whitespace_on_first_line_of_multiline_input() =>
        FPF.TruncatePreviewLine("first   \nsecond").ShouldBe("first …");

    [Fact]
    public void truncates_long_first_line_of_multiline_input()
    {
        var longLine = new string('a', 121);
        FPF.TruncatePreviewLine(longLine + "\nsecond").ShouldBe(new string('a', 120) + " …");
    }

    [Fact]
    public void returns_empty_string_unchanged() =>
        FPF.TruncatePreviewLine(string.Empty).ShouldBe(string.Empty);
}
