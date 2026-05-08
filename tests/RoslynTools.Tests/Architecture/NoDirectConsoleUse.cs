using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock: production code must not reference <c>System.Console</c> directly —
///     every write goes through <c>RtConsole</c> / <c>RtLog</c> / <c>IRtConsole</c> so that
///     daemon-mode redirection (<c>DaemonPipeConsole</c>) actually intercepts the output. A direct
///     <c>Console.WriteLine</c> in daemon mode is silently dropped: the daemon process is spawned
///     with <c>CreateNoWindow=true</c> and its <c>Console.Out</c>/<c>Error</c> handles are detached.
/// </summary>
public sealed class NoDirectConsoleUse
{
    [Fact]
    public void production_code_does_not_reference_system_console_directly()
    {
        var srcDir = LocateSrcDirectory();

        // RtConsole.cs IS the bridge — direct System.Console use there is the entire point.
        // Every other production file must go through the abstraction.
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(srcDir, "RoslynTools", "Core", "RtConsole.cs"),
            // IRtConsole.cs defines the default Out implementation for backward-compatible
            // addition of the Out member without requiring all implementors to change.
            Path.Combine(srcDir, "RoslynTools", "Core", "IRtConsole.cs")
        };

        // Console members that are NOT I/O channels — using these is fine because they don't
        // bypass RtConsole's output redirection. Add sparingly and with justification.
        var allowedMembers = new HashSet<string>(StringComparer.Ordinal)
        {
            "CancelKeyPress" // event subscription for SIGINT/Ctrl+C; not an output channel.
        };

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (allowedFiles.Contains(file)) continue;

            // Skip generated files (obj/, *.g.cs).
            var rel = Path.GetRelativePath(srcDir, file);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("obj")) continue;
            if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)) continue;

            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            // A `Console.X` access (or `Console.Out.Y`, `Console.Error.WriteLine`, etc.)
            // appears in the AST as a MemberAccessExpression whose Expression is the
            // bare identifier `Console`. The chained variants nest the inner one inside
            // a wider MemberAccess, so iterating ALL MemberAccessExpressions catches them all.
            foreach (var node in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                if (node.Expression is IdentifierNameSyntax id && id.Identifier.Text == "Console")
                {
                    var member = node.Name.Identifier.Text;
                    if (allowedMembers.Contains(member)) continue;

                    var pos = node.GetLocation().GetLineSpan().StartLinePosition;
                    violations.Add($"{rel}({pos.Line + 1}): Console.{member}");
                }
        }

        Assert.True(
            violations.Count == 0,
            "Production code must use RtConsole / RtLog / IRtConsole instead of System.Console directly.\n" +
            "Daemon mode redirects RtConsole through a named pipe; direct Console.* calls are\n" +
            "silently dropped because the daemon process has no attached console.\n\n" +
            "Offending sites:\n  " + string.Join("\n  ", violations));
    }

    private static string LocateSrcDirectory()
    {
        // Walk up from the test bin directory until we find the repo root (RoslynTools.slnx).
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "RoslynTools.slnx")))
                return Path.Combine(dir, "src");
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate RoslynTools.slnx walking up from " + AppContext.BaseDirectory);
    }
}
