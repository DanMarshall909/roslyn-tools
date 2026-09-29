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
        lines.ShouldNotContain(line => line.StartsWith("@@", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(6, 1)]
    [InlineData(7, 2)]
    public void hunk_merging_respects_six_lines_of_shared_context(int gap, int expectedHunks)
    {
        var context = string.Join('\n', Enumerable.Range(1, gap).Select(index => $"context-{index}"));
        new ConsoleSolutionWriteOutput().DocumentDiff(
            $"before\n{context}\nafter", $"updated-before\n{context}\nupdated-after", "test.cs");

        var lines = _output.ToString().Split('\n');
        lines.Count(line => line.StartsWith("@@", StringComparison.Ordinal)).ShouldBe(expectedHunks);
        if (gap == 6)
            lines.ShouldContain("@@ -1,8 +1,8 @@");
    }

    [Theory]
    [InlineData(true, "@@ -14,7 +15,7 @@")]
    [InlineData(false, "@@ -14,7 +13,7 @@")]
    public void later_hunks_report_original_and_updated_line_numbers(bool insert, string expectedHeader)
    {
        var original = Enumerable.Range(1, 20).Select(number => number.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var updated = new List<string>(original);
        if (insert)
            updated.Insert(1, "inserted");
        else
            updated.RemoveAt(1);
        updated[updated.IndexOf("17")] = "changed";

        new ConsoleSolutionWriteOutput().DocumentDiff(string.Join('\n', original), string.Join('\n', updated), "test.cs");

        var lines = _output.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        lines.ShouldContain("--- test.cs");
        lines.ShouldContain("+++ test.cs");
        lines.ShouldContain(expectedHeader);
        lines.ShouldContain(" 16");
        lines.ShouldContain("-17");
        lines.ShouldContain("+changed");
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
