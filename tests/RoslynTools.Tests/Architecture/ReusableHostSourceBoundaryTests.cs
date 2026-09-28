using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class ReusableHostSourceBoundaryTests
{
    private static readonly HashSet<string> AmbientHostTypes =
    [
        "RtConsole", "RtLog", "RtContext", "RtResultRenderer", "RtSerializer", "StructuredOutput"
    ];

    private static readonly HashSet<string> ServiceForbiddenTypes =
        new(AmbientHostTypes, StringComparer.Ordinal) { "Process", "ProcessStartInfo" };

    private static readonly string[] HostImportRoots =
    [
        "System.CommandLine", "Spectre.Console", "ModelContextProtocol", "Microsoft.Extensions.Hosting"
    ];

    [Fact]
    public void reusable_source_does_not_use_ambient_host_adapters_or_service_processes()
    {
        var reusableRoot = Path.Combine(FindRepositoryRoot(), "src", "RoslynTools");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(reusableRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(reusableRoot, file);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            if (parts.Any(part => part is "bin" or "obj"))
                continue;

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot();
            foreach (var directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                var name = directive.Name?.ToString();
                if (name is not null && HostImportRoots.Any(host =>
                        name == host || name.StartsWith(host + ".", StringComparison.Ordinal)))
                    violations.Add($"{relative}:{Line(directive)} using {name}");
            }

            var forbiddenNames = parts[0] == "Services"
                ? ServiceForbiddenTypes
                : AmbientHostTypes;
            violations.AddRange(root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => forbiddenNames.Contains(identifier.Identifier.ValueText))
                .Select(identifier => $"{relative}:{Line(identifier)} {identifier.Identifier.ValueText}"));
        }

        violations.Order(StringComparer.Ordinal).ShouldBeEmpty();
    }

    private static int Line(Microsoft.CodeAnalysis.SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
