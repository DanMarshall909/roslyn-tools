using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock (EP-008 / US-054): production code outside the
///     <c>SolutionChangeWriter</c> adapter must not call <c>SolutionWriter.ApplyChangesAsync</c>
///     directly. The DI-registered <c>ISolutionChangeWriter</c> is the only sanctioned
///     production path so mutation tests can stub writes without disk I/O.
/// </summary>
public sealed class NoDirectSolutionWriterUse
{
    [Fact]
    public void production_code_does_not_call_static_solution_writer_apply_changes_async()
    {
        var srcDir = LocateSrcDirectory();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir, file);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("obj")) continue;
            if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)) continue;

            // The adapter and SolutionWriter itself are allowed to reference the static API.
            var name = Path.GetFileName(file);
            if (name.Equals("SolutionChangeWriter.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals("SolutionWriter.cs", StringComparison.OrdinalIgnoreCase)) continue;

            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!IsStaticSolutionWriterApplyChangesAsyncCall(invocation))
                    continue;

                var containingType = invocation.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (containingType is null)
                    continue;

                var pos = invocation.GetLocation().GetLineSpan().StartLinePosition;
                violations.Add($"{rel}({pos.Line + 1}): {containingType.Identifier.Text}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Production code must call ISolutionChangeWriter, not the static SolutionWriter.ApplyChangesAsync, "
            + "so mutation tests can stub the writer and DI-managed writers can layer policy.\n\n"
            + "Offending sites:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void solution_change_writer_uses_the_legacy_static_writer_only_for_dry_runs()
    {
        var adapterPath = Path.Combine(LocateSrcDirectory(), "RoslynTools", "Core", "SolutionChangeWriter.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(adapterPath)).GetCompilationUnitRoot();
        var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(IsStaticSolutionWriterApplyChangesAsyncCall);

        foreach (var call in calls)
            Assert.Contains(call.ArgumentList.Arguments, argument =>
                argument.NameColon?.Name.Identifier.Text == "dryRun"
                && argument.Expression.IsKind(SyntaxKind.TrueLiteralExpression));
    }

    private static bool IsStaticSolutionWriterApplyChangesAsyncCall(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: "SolutionWriter" },
            Name.Identifier.Text: "ApplyChangesAsync"
        };
    }

    private static string LocateSrcDirectory()
    {
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
