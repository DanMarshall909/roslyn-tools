using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock: test source must not hardcode a build configuration
///     ("Debug"/"Release") or a target-framework moniker ("net10.0", ...) as a
///     <c>Path.Combine</c> segment when resolving another project's build output.
///     CI builds Release while local dev builds Debug, and a hardcoded value only
///     ever matches one of them — this exact mistake hid 8 CI failures behind a
///     Debug-only-built <c>rt.dll</c> path until rt-006 traced it. Derive both from
///     the running test assembly's own <c>bin/&lt;Configuration&gt;/&lt;TFM&gt;/</c>
///     path instead, the way <c>RtCliHost</c> does.
/// </summary>
public sealed class NoHardcodedBuildOutputPathInTests
{
    private static readonly Regex TfmPattern = new(@"^net\d+(\.\d+)?$", RegexOptions.Compiled);

    [Fact]
    public void test_source_does_not_hardcode_build_configuration_or_tfm_in_a_bin_path()
    {
        var testsDir = LocateTestsDirectory();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(testsDir, file);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("obj")) continue;

            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.Text: "Path" },
                        Name.Identifier.Text: "Combine"
                    })
                    continue;

                var literals = invocation.ArgumentList.Arguments
                    .Select(a => a.Expression)
                    .OfType<LiteralExpressionSyntax>()
                    .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
                    .Select(l => l.Token.ValueText)
                    .ToList();

                var hasBinSegment = literals.Any(v => v == "bin");
                var hasConfigOrTfmSegment = literals.Any(v => v is "Debug" or "Release" || TfmPattern.IsMatch(v));

                if (hasBinSegment && hasConfigOrTfmSegment)
                {
                    var pos = invocation.GetLocation().GetLineSpan().StartLinePosition;
                    violations.Add($"{rel}({pos.Line + 1}): {invocation}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Test source must not hardcode a build configuration (\"Debug\"/\"Release\") or target-framework\n" +
            "segment in a Path.Combine(...) call that resolves another project's bin output. CI builds\n" +
            "Release, local dev builds Debug — a hardcoded value only ever matches one of them (rt-006).\n" +
            "Derive both from AppContext.BaseDirectory the way RtCliHost does.\n\n" +
            "Offending sites:\n  " + string.Join("\n  ", violations));
    }

    private static string LocateTestsDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "RoslynTools.slnx")))
                return Path.Combine(dir, "tests");
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate RoslynTools.slnx walking up from " + AppContext.BaseDirectory);
    }
}
