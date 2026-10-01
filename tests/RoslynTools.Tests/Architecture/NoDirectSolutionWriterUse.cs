using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock (EP-008 / US-054): production code outside the
///     <c>SolutionChangeWriter</c> adapter must not call <c>SolutionWriter.PreviewChangesAsync</c>
///     directly. The DI-registered <c>ISolutionChangeWriter</c> is the only sanctioned
///     production path so mutation tests can stub writes without disk I/O.
/// </summary>
public sealed class NoDirectSolutionWriterUse
{
    [Fact]
    public void production_code_does_not_call_static_solution_writer_preview_directly()
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
                if (!IsStaticSolutionWriterPreviewChangesAsyncCall(invocation))
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
            "Production code must call ISolutionChangeWriter, not the static SolutionWriter.PreviewChangesAsync, "
            + "so mutation tests can stub the writer and DI-managed writers can layer policy.\n\n"
            + "Offending sites:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void solution_change_writer_uses_the_static_writer_only_in_its_preview_branch()
    {
        var adapterPath = Path.Combine(LocateSrcDirectory(), "RoslynTools", "Core", "SolutionChangeWriter.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(adapterPath)).GetCompilationUnitRoot();
        var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(IsStaticSolutionWriterPreviewChangesAsyncCall).ToArray();

        Assert.Single(calls);
        Assert.Contains(calls[0].Ancestors().OfType<IfStatementSyntax>(), statement =>
            statement.Condition is IdentifierNameSyntax { Identifier.Text: "dryRun" });
    }

    [Fact]
    public void static_solution_writer_has_only_a_preview_method_and_no_filesystem_mutators()
    {
        var writerPath = Path.Combine(LocateSrcDirectory(), "RoslynTools", "Core", "SolutionWriter.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(writerPath)).GetCompilationUnitRoot();
        var writer = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.Text == "SolutionWriter");
        var method = Assert.Single(writer.Members.OfType<MethodDeclarationSyntax>());
        Assert.Equal("PreviewChangesAsync", method.Identifier.Text);
        Assert.Equal(3, method.ParameterList.Parameters.Count);
        Assert.DoesNotContain(method.DescendantNodes().OfType<InvocationExpressionSyntax>(), invocation =>
            invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.Text: "File" or "Directory" }
            });
    }

    private static bool IsStaticSolutionWriterPreviewChangesAsyncCall(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: "SolutionWriter" },
            Name.Identifier.Text: "PreviewChangesAsync"
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
