using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class SolutionWriter : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly StringWriter _output;

    public SolutionWriter()
    {
        _originalOut = Console.Out;
        _output = new StringWriter();
        Console.SetOut(_output);
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void shows_only_the_inserted_line()
    {
        var oldText = "a\nb\nc";
        var newText = "a\nX\nb\nc";

        new ConsoleSolutionWriteOutput().DocumentDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var diffLines = lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal)).ToArray();
        var removedLines = lines.Where(l => l.StartsWith('-') && !l.StartsWith("---", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(diffLines);
        diffLines[0].ShouldContain("X", Case.Sensitive);
        removedLines.ShouldBeEmpty();
    }

    [Fact]
    public void shows_only_the_deleted_line()
    {
        var oldText = "a\nX\nb";
        var newText = "a\nb";

        new ConsoleSolutionWriteOutput().DocumentDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var addedLines = lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal))
            .ToArray();
        var removedLines = lines.Where(l => l.StartsWith('-') && !l.StartsWith("---", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(removedLines);
        removedLines[0].ShouldContain("X", Case.Sensitive);
        addedLines.ShouldBeEmpty();
    }

    [Fact]
    public void shows_the_deleted_and_inserted_lines()
    {
        var oldText = "a\nold\nc";
        var newText = "a\nnew\nc";

        new ConsoleSolutionWriteOutput().DocumentDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var addedLines = lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal))
            .ToArray();
        var removedLines = lines.Where(l => l.StartsWith('-') && !l.StartsWith("---", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(removedLines);
        Assert.Single(addedLines);
        removedLines[0].ShouldContain("old", Case.Sensitive);
        addedLines[0].ShouldContain("new", Case.Sensitive);
    }

    [Fact]
    public void produces_no_diff_lines()
    {
        var text = "a\nb\nc";

        new ConsoleSolutionWriteOutput().DocumentDiff(text, text, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var diffLines = lines.Where(l =>
            (l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal)) ||
            (l.StartsWith('-') && !l.StartsWith("---", StringComparison.Ordinal))).ToArray();

        diffLines.ShouldBeEmpty();
    }

    [Fact]
    public void produces_separate_hunks()
    {
        // Two changes separated by >6 unchanged lines (2*contextSize) so they form separate hunks
        var oldText = "a\nb\nc\nd\ne\nf\ng\nh\ni\nj\nk\nl\nm\nn\no";
        var newText = "a\nX\nc\nd\ne\nf\ng\nh\ni\nj\nk\nl\nm\nY\no";

        new ConsoleSolutionWriteOutput().DocumentDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var hunkHeaders = output.Split('\n')
            .Where(l => l.StartsWith("@@", StringComparison.Ordinal))
            .ToArray();

        Assert.True(hunkHeaders.Length >= 2,
            $"Expected at least 2 hunk headers, got {hunkHeaders.Length}. Output:\n{output}");
    }
}
