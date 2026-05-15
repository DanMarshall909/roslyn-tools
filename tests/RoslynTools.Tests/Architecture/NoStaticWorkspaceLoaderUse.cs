using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock: production classes must not call the static
///     <c>WorkspaceLoader.LoadAsync</c> compatibility API. New code should use the
///     injectable <c>WorkspaceLoader</c> instance path so daemon workspace caching can work.
/// </summary>
public sealed class NoStaticWorkspaceLoaderUse
{
    [Fact]
    public void production_classes_do_not_call_static_workspace_loader_load_async()
    {
        var srcDir = LocateSrcDirectory();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir, file);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("obj")) continue;
            if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)) continue;

            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!IsStaticWorkspaceLoaderLoadAsyncCall(invocation))
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
            "Production classes must not call static WorkspaceLoader.LoadAsync. "
            + "Use the injectable WorkspaceLoader instance path instead so daemon workspace caching can retain a loaded solution.\n\n"
            + "Offending sites:\n  " + string.Join("\n  ", violations));
    }

    private static bool IsStaticWorkspaceLoaderLoadAsyncCall(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: "WorkspaceLoader" },
            Name.Identifier.Text: "LoadAsync"
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
