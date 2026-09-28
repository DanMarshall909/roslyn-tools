using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock: production code must not reference <c>System.Console</c>
///     directly outside the transitional console bridge, and services must not
///     call ambient console or log adapters. Hosts own rendering and logging.
/// </summary>
public sealed class NoDirectConsoleUse
{
    [Fact]
    public void services_do_not_call_ambient_output_adapters()
    {
        var services = Path.Combine(LocateSrcDirectory(), "RoslynTools", "Services");
        var violations = Directory.EnumerateFiles(services, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(services, path)
                .Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .SelectMany(path =>
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
                var ambientCalls = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Where(node => node.Expression is IdentifierNameSyntax identifier
                        && identifier.Identifier.Text is "RtConsole" or "RtLog" or "RtResultRenderer")
                    .Select(node => $"{Path.GetRelativePath(services, path)}:"
                        + $"{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"
                        + $" {node.Expression}.{node.Name}");
                var consoleLoggerConstruction = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                    .Where(node => node.Type is GenericNameSyntax type
                        && type.Identifier.Text == "RtConsoleLogger")
                    .Select(node => $"{Path.GetRelativePath(services, path)}:"
                        + $"{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"
                        + " new RtConsoleLogger<T>");
                return ambientCalls.Concat(consoleLoggerConstruction)
                    .ToArray();
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void services_do_not_set_process_exit_code()
    {
        var services = Path.Combine(LocateSrcDirectory(), "RoslynTools", "Services");
        var violations = Directory.EnumerateFiles(services, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(services, path)
                .Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .SelectMany(path =>
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
                return root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                    .Where(node => node.Left is MemberAccessExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.Text: "Environment" },
                        Name.Identifier.Text: "ExitCode"
                    })
                    .Select(node => $"{Path.GetRelativePath(services, path)}:"
                        + $"{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}")
                    .ToArray();
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void workspace_cache_does_not_use_the_ambient_logger()
    {
        var path = Path.Combine(LocateSrcDirectory(), "RoslynTools", "Core", "ReadOnlyWorkspaceSessions.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
        var violations = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(node => node.Expression is IdentifierNameSyntax { Identifier.Text: "RtLog" })
            .Select(node => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1)
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void reusable_logger_extensions_do_not_depend_on_the_console_bridge()
    {
        var path = Path.Combine(LocateSrcDirectory(), "RoslynTools", "Core", "RtLoggerExtensions.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
        var violations = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(node => node.Expression is IdentifierNameSyntax { Identifier.Text: "RtConsole" })
            .Select(node => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1)
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void workspace_loader_diagnostics_do_not_use_ambient_host_state()
    {
        var path = Path.Combine(LocateSrcDirectory(), "RoslynTools", "Core", "WorkspaceLoader.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
        var violations = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(node => node.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.Text is "RtLog" or "RtContext")
            .Select(node => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1)
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void production_code_does_not_reference_system_console_directly()
    {
        var srcDir = LocateSrcDirectory();

        // RtConsole.cs IS the bridge — direct System.Console use there is the entire point.
        // Every other production file must go through an output abstraction.
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
            "Production code must use a host output abstraction instead of System.Console directly.\n\n" +
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
