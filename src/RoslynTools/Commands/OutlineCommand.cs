using System.CommandLine;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class OutlineCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    internal static Command Build()
    {
        var targetArg   = new Argument<string>("target", "File path or namespace to outline");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");

        var cmd = new Command("outline", "List all types and member signatures in a file or namespace (JSON)")
        {
            targetArg, solutionOpt
        };

        cmd.SetHandler(async (target, solution) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                List<Dictionary<string, object?>> result;

                if (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || Path.Exists(target))
                    result = await OutlineFileAsync(sln, target);
                else
                    result = await OutlineNamespaceAsync(sln, target);

                Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
            }
        },
        targetArg, solutionOpt);

        return cmd;
    }

    private static async Task<List<Dictionary<string, object?>>> OutlineFileAsync(Solution solution, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var results = new List<Dictionary<string, object?>>();

        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (!string.Equals(doc.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var semanticModel = await doc.GetSemanticModelAsync();
                if (semanticModel == null) continue;

                var root = await doc.GetSyntaxRootAsync();
                if (root == null) continue;

                var declaredSymbols = root.DescendantNodes()
                    .Select(n => semanticModel.GetDeclaredSymbol(n))
                    .Where(s => s is INamedTypeSymbol)
                    .Cast<INamedTypeSymbol>()
                    .Distinct(SymbolEqualityComparer.Default)
                    .Cast<INamedTypeSymbol>();

                foreach (var type in declaredSymbols)
                    results.Add(BuildTypeOutline(type));
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException($"No types found in file: {fullPath}");

        return results;
    }

    private static async Task<List<Dictionary<string, object?>>> OutlineNamespaceAsync(Solution solution, string namespaceName)
    {
        var results = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            var nsSymbols = FindNamespaceTypes(compilation.GlobalNamespace, namespaceName);
            foreach (var type in nsSymbols)
            {
                var key = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (seen.Add(key))
                    results.Add(BuildTypeOutline(type));
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException($"No types found in namespace: {namespaceName}");

        return results;
    }

    private static IEnumerable<INamedTypeSymbol> FindNamespaceTypes(INamespaceSymbol ns, string targetName)
    {
        if (ns.ToDisplayString() == targetName)
        {
            foreach (var type in ns.GetTypeMembers())
                yield return type;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in FindNamespaceTypes(child, targetName))
                yield return type;
        }
    }

    private static Dictionary<string, object?> BuildTypeOutline(INamedTypeSymbol type)
    {
        var outline = new Dictionary<string, object?>
        {
            ["name"] = type.Name,
            ["fqn"] = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            ["kind"] = type.TypeKind.ToString(),
            ["accessibility"] = type.DeclaredAccessibility.ToString(),
        };

        if (type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object)
            outline["baseType"] = type.BaseType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        var interfaces = type.Interfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .ToList();
        if (interfaces.Count > 0)
            outline["interfaces"] = interfaces;

        var members = type.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared)
            .Select(m => new Dictionary<string, object?>
            {
                ["name"] = m.Name,
                ["kind"] = m.Kind.ToString(),
                ["signature"] = m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                ["accessibility"] = m.DeclaredAccessibility.ToString(),
            })
            .ToList();
        outline["members"] = members;

        return outline;
    }
}
