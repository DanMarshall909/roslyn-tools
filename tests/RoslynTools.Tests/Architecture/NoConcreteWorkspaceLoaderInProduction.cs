using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock (EP-008 / US-053): production code outside
///     the composition roots and <c>WorkspaceLoader.cs</c> must depend on <c>IWorkspaceSessionFactory</c>,
///     not the concrete <c>WorkspaceLoader</c> type. Concrete references defeat
///     the injection seam and block daemon workspace caching.
/// </summary>
public sealed class NoConcreteWorkspaceLoaderInProduction
{
    [Fact]
    public void production_code_does_not_reference_concrete_workspace_loader()
    {
        var srcDir = LocateSrcDirectory();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir, file);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("obj")) continue;
            if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)) continue;

            // Sanctioned references to the concrete type:
            //  - WorkspaceLoader.cs declares the class
            //  - IWorkspaceSessionFactory.cs hosts the CreateDefault factory
            //  - HostServiceRegistration.cs is the shared composition root that wires the interface
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("WorkspaceLoader.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (fileName.Equals("IWorkspaceSessionFactory.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (rel == Path.Combine("RoslynTools.Hosting", "HostServiceRegistration.cs")) continue;

            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            foreach (var ident in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (ident.Identifier.Text != "WorkspaceLoader")
                    continue;

                // Skip cref / nameof / doc-comment references — they're inert and don't create coupling.
                if (ident.Ancestors().OfType<CrefSyntax>().Any())
                    continue;
                if (ident.Parent is InvocationExpressionSyntax inv
                    && inv.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" })
                    continue;

                var pos = ident.GetLocation().GetLineSpan().StartLinePosition;
                violations.Add($"{rel}({pos.Line + 1}): {ident}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Production code must depend on IWorkspaceSessionFactory, not the concrete WorkspaceLoader type, "
            + "so daemon workspace caching and test-time stubbing remain possible.\n\n"
            + "Offending sites:\n  " + string.Join("\n  ", violations));
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
