using FPF = RoslynTools.Core.PreviewFormatter;

namespace RoslynTools.Tests.Infrastructure;

public class PreviewFormatter_TruncatePreviewLine
{
    [Fact]
    public void returns_short_single_line_unchanged() =>
        Assert.Equal("int x = 1;", FPF.TruncatePreviewLine("int x = 1;"));

    [Fact]
    public void returns_exactly_120_chars_unchanged()
    {
        var input = new string('a', 120);
        Assert.Equal(input, FPF.TruncatePreviewLine(input));
    }

    [Fact]
    public void truncates_121_char_line_to_120_plus_ellipsis()
    {
        var input = new string('a', 121);
        Assert.Equal(new string('a', 120) + " …", FPF.TruncatePreviewLine(input));
    }

    [Fact]
    public void takes_first_line_on_lf_input() =>
        Assert.Equal("first …", FPF.TruncatePreviewLine("first\nsecond"));

    [Fact]
    public void takes_first_line_on_crlf_input() =>
        Assert.Equal("first …", FPF.TruncatePreviewLine("first\r\nsecond"));

    [Fact]
    public void takes_first_line_on_cr_input() =>
        Assert.Equal("first …", FPF.TruncatePreviewLine("first\rsecond"));

    [Fact]
    public void trims_trailing_whitespace_on_first_line_of_multiline_input() =>
        Assert.Equal("first …", FPF.TruncatePreviewLine("first   \nsecond"));

    [Fact]
    public void truncates_long_first_line_of_multiline_input()
    {
        var longLine = new string('a', 121);
        Assert.Equal(new string('a', 120) + " …", FPF.TruncatePreviewLine(longLine + "\nsecond"));
    }

    [Fact]
    public void returns_empty_string_unchanged() =>
        Assert.Equal(string.Empty, FPF.TruncatePreviewLine(string.Empty));
}
