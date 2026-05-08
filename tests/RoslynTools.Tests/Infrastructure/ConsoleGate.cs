using System.Text.RegularExpressions;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     Architecture gate: only <c>RtConsole.cs</c> may reference <c>Console</c> directly.
///     See docs/logging-standards.md.
/// </summary>
public class ConsoleGate
{
    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "RtConsole.cs",
            "IRtConsole.cs" // default Out implementation uses Console.Out as the base case
        };

    [Fact]
    public void only_rt_console_may_reference_console_directly()
    {
        var srcRoot = FindSrcRoot();
        var files = Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGeneratedOrObj(f));

        var violations = new List<string>();

        foreach (var file in files)
        {
            if (Allowed.Contains(Path.GetFileName(file)))
                continue;

            var source = File.ReadAllText(file);
            var stripped = StripStringLiterals(source);

            if (ContainsConsoleMember(stripped))
                violations.Add(Path.GetRelativePath(srcRoot, file));
        }

        Assert.True(violations.Count == 0,
            $"""
             {violations.Count} file(s) reference Console.* directly outside the allowlist.
             Route all console I/O through RtConsole — see docs/logging-standards.md.

             Violations:
             {string.Join(Environment.NewLine, violations.Select(v => "  " + v))}
             """);
    }

    /// <summary>
    ///     Replaces string literal content with whitespace so that occurrences of "Console."
    ///     inside example strings or comments don't trigger false positives.
    /// </summary>
    private static string StripStringLiterals(string source)
    {
        // Strip line comments
        source = Regex.Replace(source, @"//[^\r\n]*", " ");

        // Strip block comments
        source = Regex.Replace(source, @"/\*.*?\*/", " ",
            RegexOptions.Singleline);

        // Strip verbatim strings @"..." (non-greedy, handles escaped quotes "" inside)
        source = Regex.Replace(source, @"@""(?:[^""]|"""")*""", "\"\"");

        // Strip raw string literals """...""" (3+ quotes)
        source = Regex.Replace(source, "\"\"\".*?\"\"\"", "\"\"\"\"\"\"",
            RegexOptions.Singleline);

        // Strip regular string literals "..." (handles \" escapes)
        source = Regex.Replace(source, @"""(?:[^""\\]|\\.)*""", "\"\"");

        return source;
    }

    private static bool ContainsConsoleMember(string source)
    {
        return Regex.IsMatch(source, @"\bConsole\s*\.");
    }

    private static bool IsGeneratedOrObj(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
               path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
               path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindSrcRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate 'src' directory from test base directory.");
    }
}
