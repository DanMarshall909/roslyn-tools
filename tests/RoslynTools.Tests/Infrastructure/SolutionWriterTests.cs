using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

public class SolutionWriterTests : IDisposable
{
    private readonly StringWriter _output;
    private readonly TextWriter _originalOut;

    public SolutionWriterTests()
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
    public void Shows_only_the_inserted_line()
    {
        var oldText = "a\nb\nc";
        var newText = "a\nX\nb\nc";

        SolutionWriter.PrintDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var diffLines = lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal)).ToArray();
        var removedLines = lines.Where(l => l.StartsWith('-') && !l.StartsWith("---", StringComparison.Ordinal)).ToArray();

        Assert.Single(diffLines);
        Assert.Contains("X", diffLines[0]);
        Assert.Empty(removedLines);
    }

    [Fact]
    public void Shows_only_the_deleted_line()
    {
        var oldText = "a\nX\nb";
        var newText = "a\nb";

        SolutionWriter.PrintDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var addedLines = lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal)).ToArray();
        var removedLines = lines.Where(l => l.StartsWith('-') && !l.StartsWith("---", StringComparison.Ordinal)).ToArray();

        Assert.Single(removedLines);
        Assert.Contains("X", removedLines[0]);
        Assert.Empty(addedLines);
    }

    [Fact]
    public void Shows_the_deleted_and_inserted_lines()
    {
        var oldText = "a\nold\nc";
        var newText = "a\nnew\nc";

        SolutionWriter.PrintDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var addedLines = lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal)).ToArray();
        var removedLines = lines.Where(l => l.StartsWith('-') && !l.StartsWith("---", StringComparison.Ordinal)).ToArray();

        Assert.Single(removedLines);
        Assert.Single(addedLines);
        Assert.Contains("old", removedLines[0]);
        Assert.Contains("new", addedLines[0]);
    }

    [Fact]
    public void Produces_no_diff_lines()
    {
        var text = "a\nb\nc";

        SolutionWriter.PrintDiff(text, text, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var diffLines = lines.Where(l =>
            (l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal)) ||
            (l.StartsWith('-') && !l.StartsWith("---", StringComparison.Ordinal))).ToArray();

        Assert.Empty(diffLines);
    }

    [Fact]
    public void Produces_separate_hunks()
    {
        // Two changes separated by >6 unchanged lines (2*contextSize) so they form separate hunks
        var oldText = "a\nb\nc\nd\ne\nf\ng\nh\ni\nj\nk\nl\nm\nn\no";
        var newText = "a\nX\nc\nd\ne\nf\ng\nh\ni\nj\nk\nl\nm\nY\no";

        SolutionWriter.PrintDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var hunkHeaders = output.Split('\n')
            .Where(l => l.StartsWith("@@", StringComparison.Ordinal))
            .ToArray();

        Assert.True(hunkHeaders.Length >= 2, $"Expected at least 2 hunk headers, got {hunkHeaders.Length}. Output:\n{output}");
    }
}
