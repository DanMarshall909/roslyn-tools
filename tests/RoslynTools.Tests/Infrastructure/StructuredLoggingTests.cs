using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
/// Architecture gate: all RtLog.* and RtConsole.Log template arguments must be plain string
/// literals — no interpolated strings, concatenation, or variables.
/// See docs/logging-standards.md.
/// </summary>
public class StructuredLoggingTests
{
    [Fact]
    public void Diagnostic_log_calls_must_use_plain_string_literal_templates()
    {
        var srcRoot = FindSrcRoot();
        // RtLog.cs is the pass-through adapter — its RtConsole.Log calls just forward
        // whatever template was given by the caller; violations are at the call sites.
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RtLog.cs" };

        var files = Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGeneratedOrObj(f) && !excluded.Contains(Path.GetFileName(f)));

        var violations = new List<string>();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source, path: file);
            var root = tree.GetRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var templateArg = GetTemplateArg(invocation);
                if (templateArg is null)
                    continue;

                if (templateArg is not LiteralExpressionSyntax)
                {
                    var lineSpan = tree.GetLineSpan(templateArg.Span);
                    violations.Add(
                        $"  {Path.GetRelativePath(srcRoot, file)}:{lineSpan.StartLinePosition.Line + 1}  " +
                        $"{invocation.Expression}({templateArg})");
                }
            }
        }

        Assert.True(violations.Count == 0,
            violations.Count + " non-literal template argument(s) in diagnostic log call(s).\n" +
            "The template must be a plain string literal — no interpolation, concatenation, or variables.\n" +
            "Use named placeholders: RtLog.Info(\"Updated {FilePath}\", path)\n" +
            "See docs/logging-standards.md for guidance.\n\n" +
            "Violations:\n" + string.Join(Environment.NewLine, violations));
    }

    // Returns the template argument for known log call shapes, or null if not a log call.
    // RtLog.Info/Warning/Error(template, args...)  -> arg[0]
    // RtConsole.Log(level, template, args...)      -> arg[1]
    private static ExpressionSyntax? GetTemplateArg(InvocationExpressionSyntax invocation)
    {
        var text = invocation.Expression.ToString();
        var args = invocation.ArgumentList.Arguments;
        return text switch
        {
            "RtLog.Info" or "RtLog.Warning" or "RtLog.Error" when args.Count >= 1
                => args[0].Expression,
            "RtConsole.Log" when args.Count >= 2
                => args[1].Expression,
            _ => null
        };
    }

    private static bool IsGeneratedOrObj(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
        path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
        path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);

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
