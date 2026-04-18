using RoslynTools.Core;

namespace RoslynTools.Tests;

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
    public void PrintDiff_SingleLineInsertion_ShowsOnlyInsertedLine()
    {
        var oldText = "a\nb\nc";
        var newText = "a\nX\nb\nc";

        SolutionWriter.PrintDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var diffLines = lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++")).ToArray();
        var removedLines = lines.Where(l => l.StartsWith('-') && !l.StartsWith("---")).ToArray();

        Assert.Single(diffLines);
        Assert.Contains("X", diffLines[0]);
        Assert.Empty(removedLines);
    }

    [Fact]
    public void PrintDiff_SingleLineDeletion_ShowsOnlyDeletedLine()
    {
        var oldText = "a\nX\nb";
        var newText = "a\nb";

        SolutionWriter.PrintDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var addedLines = lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++")).ToArray();
        var removedLines = lines.Where(l => l.StartsWith('-') && !l.StartsWith("---")).ToArray();

        Assert.Single(removedLines);
        Assert.Contains("X", removedLines[0]);
        Assert.Empty(addedLines);
    }

    [Fact]
    public void PrintDiff_ModifiedLine_ShowsDeleteAndInsert()
    {
        var oldText = "a\nold\nc";
        var newText = "a\nnew\nc";

        SolutionWriter.PrintDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var addedLines = lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++")).ToArray();
        var removedLines = lines.Where(l => l.StartsWith('-') && !l.StartsWith("---")).ToArray();

        Assert.Single(removedLines);
        Assert.Single(addedLines);
        Assert.Contains("old", removedLines[0]);
        Assert.Contains("new", addedLines[0]);
    }

    [Fact]
    public void PrintDiff_IdenticalText_ProducesNoDiffLines()
    {
        var text = "a\nb\nc";

        SolutionWriter.PrintDiff(text, text, "test.cs");

        var output = _output.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var diffLines = lines.Where(l =>
            (l.StartsWith('+') && !l.StartsWith("+++")) ||
            (l.StartsWith('-') && !l.StartsWith("---"))).ToArray();

        Assert.Empty(diffLines);
    }

    [Fact]
    public void PrintDiff_MultipleChanges_ProducesSeparateHunks()
    {
        // Two changes separated by >6 unchanged lines (2*contextSize) so they form separate hunks
        var oldText = "a\nb\nc\nd\ne\nf\ng\nh\ni\nj\nk\nl\nm\nn\no";
        var newText = "a\nX\nc\nd\ne\nf\ng\nh\ni\nj\nk\nl\nm\nY\no";

        SolutionWriter.PrintDiff(oldText, newText, "test.cs");

        var output = _output.ToString();
        var hunkHeaders = output.Split('\n')
            .Where(l => l.StartsWith("@@"))
            .ToArray();

        Assert.True(hunkHeaders.Length >= 2, $"Expected at least 2 hunk headers, got {hunkHeaders.Length}. Output:\n{output}");
    }
}
