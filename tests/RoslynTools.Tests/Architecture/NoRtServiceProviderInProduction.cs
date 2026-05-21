using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock (US-072): production code must not use the ambient
///     <c>RtServiceProvider</c> service-locator. Commands and services depend on
///     their collaborators via constructor injection; <c>RtServiceProvider.Get</c>
///     and <c>RtServiceProvider.GetOrDefault</c> are migration scaffolding that
///     gets deleted at the end of this story.
/// </summary>
public sealed class NoRtServiceProviderInProduction
{
    [Fact(Skip = "US-072: unskip once all RtServiceProvider.Get/.GetOrDefault sites migrate to ctor injection.")]
    public void production_code_does_not_call_rt_service_provider_locator()
    {
        var srcDir = LocateSrcDirectory();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir, file);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("obj")) continue;
            if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)) continue;

            // RtServiceProvider.cs declares the type; allow it to mention its own name.
            if (Path.GetFileName(file).Equals("RtServiceProvider.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!IsRtServiceProviderLocatorCall(invocation))
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
            "Production code must depend on its collaborators via constructor injection — the static "
            + "RtServiceProvider.Get/.GetOrDefault locator is forbidden so that command dependencies are explicit.\n\n"
            + "Offending sites:\n  " + string.Join("\n  ", violations));
    }

    private static bool IsRtServiceProviderLocatorCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member) return false;
        if (member.Expression is not IdentifierNameSyntax { Identifier.Text: "RtServiceProvider" }) return false;

        var name = member.Name switch
        {
            GenericNameSyntax g => g.Identifier.Text,
            IdentifierNameSyntax i => i.Identifier.Text,
            _ => null
        };

        return name is "Get" or "GetOrDefault";
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
