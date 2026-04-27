using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal static class DependencyGraphCore
{
    /// <summary>
    /// Builds a type-level dependency graph and returns it in the requested format.
    /// </summary>
    internal static async Task<string> BuildGraphAsync(
        Solution sln,
        string? typeName,
        int? depth,
        string format,
        string? scope)
    {
        var allTypes = await CollectSourceTypesAsync(sln);

        var sourceTypeByName = BuildSourceTypeIndex(allTypes);
        var bodyDeps = await CollectBodyDependenciesAsync(sln, allTypes);

        IEnumerable<INamedTypeSymbol> typesToGraph = allTypes;
        if (scope != null)
            typesToGraph = allTypes.Where(t => GetNamespaceName(t).StartsWith(scope, StringComparison.Ordinal));

        if (typeName != null)
        {
            var root = allTypes.FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.Ordinal) ||
                t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Contains(typeName, StringComparison.Ordinal));

            if (root == null)
                return format.Equals("dot", StringComparison.OrdinalIgnoreCase)
                    ? "digraph { }"
                    : JsonSerializer.Serialize(new { nodes = Array.Empty<string>(), edges = Array.Empty<object>() }, SymbolFormatter.DefaultJsonOptions);

            var (nodes, edges) = BuildBfsGraph(root, depth, allTypes, sourceTypeByName, bodyDeps);
            return SerializeGraph(nodes, edges, format);
        }
        else
        {
            var (nodes, edges) = BuildFullGraph(typesToGraph, allTypes, sourceTypeByName, bodyDeps);
            return SerializeGraph(nodes, edges, format);
        }
    }

    private static Dictionary<string, INamedTypeSymbol> BuildSourceTypeIndex(List<INamedTypeSymbol> allTypes)
    {
        var index = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var t in allTypes)
            index.TryAdd(t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), t);
        return index;
    }

    private static async Task<List<INamedTypeSymbol>> CollectSourceTypesAsync(Solution sln)
    {
        var result = new List<INamedTypeSymbol>();
        var rtIgnore = RtIgnore.ForSolution(sln);

        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                continue;

            foreach (var document in project.Documents)
            {
                if (rtIgnore.IsIgnored(document.FilePath))
                    continue;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null)
                    continue;

                var root = await document.GetSyntaxRootAsync();
                if (root == null)
                    continue;

                foreach (var node in root.DescendantNodesAndSelf())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(node) as INamedTypeSymbol;
                    if (symbol == null || symbol.IsImplicitlyDeclared || symbol.TypeKind == TypeKind.Error)
                        continue;
                    result.Add(symbol);
                }
            }
        }
        return result;
    }

    private static (HashSet<string> Nodes, List<(string From, string To)> Edges) BuildFullGraph(
        IEnumerable<INamedTypeSymbol> typesToGraph,
        List<INamedTypeSymbol> allTypes,
        Dictionary<string, INamedTypeSymbol> sourceTypeByName,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> bodyDeps)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var edgeSet = new HashSet<(string, string)>();
        var edges = new List<(string From, string To)>();

        foreach (var type in typesToGraph)
        {
            var fromName = GetDisplayName(type);
            nodes.Add(fromName);

            foreach (var dep in GetDependencies(type, bodyDeps))
            {
                if (!IsSourceType(dep, sourceTypeByName))
                    continue;

                var toName = GetDisplayName(dep);
                if (fromName != toName && edgeSet.Add((fromName, toName)))
                {
                    nodes.Add(toName);
                    edges.Add((fromName, toName));
                }
            }
        }

        return (nodes, edges);
    }

    private static (HashSet<string> Nodes, List<(string From, string To)> Edges) BuildBfsGraph(
        INamedTypeSymbol rootType,
        int? maxDepth,
        List<INamedTypeSymbol> allTypes,
        Dictionary<string, INamedTypeSymbol> sourceTypeByName,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> bodyDeps)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var edgeSet = new HashSet<(string, string)>();
        var edges = new List<(string From, string To)>();
        var queue = new Queue<(INamedTypeSymbol Type, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        var rootName = GetDisplayName(rootType);
        nodes.Add(rootName);
        visited.Add(rootName);
        queue.Enqueue((rootType, 0));

        while (queue.Count > 0)
        {
            var (current, currentDepth) = queue.Dequeue();
            if (maxDepth.HasValue && currentDepth >= maxDepth.Value)
                continue;

            ExpandBfsNode(current, currentDepth, sourceTypeByName, bodyDeps, nodes, edgeSet, edges, visited, queue);
        }

        return (nodes, edges);
    }

    private static void ExpandBfsNode(
        INamedTypeSymbol current,
        int currentDepth,
        Dictionary<string, INamedTypeSymbol> sourceTypeByName,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> bodyDeps,
        HashSet<string> nodes,
        HashSet<(string, string)> edgeSet,
        List<(string From, string To)> edges,
        HashSet<string> visited,
        Queue<(INamedTypeSymbol Type, int Depth)> queue)
    {
        var fromName = GetDisplayName(current);
        foreach (var dep in GetDependencies(current, bodyDeps))
        {
            if (!IsSourceType(dep, sourceTypeByName))
                continue;

            var toName = GetDisplayName(dep);
            if (fromName == toName)
                continue;

            if (edgeSet.Add((fromName, toName)))
                edges.Add((fromName, toName));

            if (nodes.Add(toName) && visited.Add(toName))
                queue.Enqueue((dep, currentDepth + 1));
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetDependencies(
        INamedTypeSymbol type,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> bodyDeps)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dep in GetInheritanceDependencies(type))
            if (seen.Add(dep.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                yield return dep;

        foreach (var dep in GetFieldDependencies(type))
            if (seen.Add(dep.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                yield return dep;

        foreach (var dep in GetMethodDependencies(type))
            if (seen.Add(dep.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                yield return dep;

        if (bodyDeps.TryGetValue(type, out var locals))
        {
            foreach (var dep in locals)
                if (seen.Add(dep.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                    yield return dep;
        }
    }

    private static async Task<Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>> CollectBodyDependenciesAsync(
        Solution sln,
        List<INamedTypeSymbol> sourceTypes)
    {
        var result = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);

        foreach (var type in sourceTypes)
        {
            var deps = new List<INamedTypeSymbol>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                foreach (var syntaxRef in method.DeclaringSyntaxReferences)
                {
                    var node = await syntaxRef.GetSyntaxAsync();
                    if (node is not BaseMethodDeclarationSyntax decl)
                        continue;

                    SyntaxNode? body = decl.Body ?? (SyntaxNode?)decl.ExpressionBody;
                    if (body == null)
                        continue;

                    var doc = sln.GetDocument(node.SyntaxTree);
                    if (doc == null)
                        continue;
                    var model = await doc.GetSemanticModelAsync();
                    if (model == null)
                        continue;

                    foreach (var name in body.DescendantNodes().OfType<SimpleNameSyntax>())
                    {
                        if (model.GetSymbolInfo(name).Symbol is INamedTypeSymbol named &&
                            ExtractNamedType(named) is { } resolved &&
                            seen.Add(resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                        {
                            deps.Add(resolved);
                        }
                    }

                    foreach (var oce in body.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
                    {
                        if (ExtractNamedType(model.GetTypeInfo(oce).Type) is { } resolved &&
                            seen.Add(resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                        {
                            deps.Add(resolved);
                        }
                    }
                }
            }

            if (deps.Count > 0)
                result[type] = deps;
        }

        return result;
    }

    private static IEnumerable<INamedTypeSymbol> GetInheritanceDependencies(INamedTypeSymbol type)
    {
        if (type.BaseType != null && type.BaseType.SpecialType == SpecialType.None)
        {
            var dep = ExtractNamedType(type.BaseType);
            if (dep != null)
                yield return dep;
        }

        foreach (var iface in type.Interfaces)
        {
            var dep = ExtractNamedType(iface);
            if (dep != null)
                yield return dep;
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetFieldDependencies(INamedTypeSymbol type)
    {
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            var dep = ExtractNamedType(field.Type);
            if (dep != null)
                yield return dep;
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetMethodDependencies(INamedTypeSymbol type)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            var retDep = ExtractNamedType(method.ReturnType);
            if (retDep != null)
                yield return retDep;

            foreach (var param in method.Parameters)
            {
                var paramDep = ExtractNamedType(param.Type);
                if (paramDep != null)
                    yield return paramDep;
            }
        }
    }

    private static INamedTypeSymbol? ExtractNamedType(ITypeSymbol? type)
    {
        return type switch
        {
            INamedTypeSymbol named => named,
            IArrayTypeSymbol array => ExtractNamedType(array.ElementType),
            _ => null,
        };
    }

    private static bool IsSourceType(INamedTypeSymbol? type, Dictionary<string, INamedTypeSymbol> sourceTypeByName)
    {
        if (type == null || !type.Locations.Any(l => l.IsInSource))
            return false;

        var ns = GetNamespaceName(type);
        if (ns.StartsWith("System.", StringComparison.Ordinal) || ns == "System" ||
            ns.StartsWith("Microsoft.", StringComparison.Ordinal) || ns == "Microsoft")
            return type.Locations.Any(l => l.IsInSource);

        return true;
    }

    private static string GetNamespaceName(INamedTypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() ?? string.Empty;

    private static string GetDisplayName(INamedTypeSymbol type)
    {
        var ns = GetNamespaceName(type);
        return string.IsNullOrEmpty(ns) || ns == "<global namespace>"
            ? type.Name
            : type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static string SerializeGraph(HashSet<string> nodes, List<(string From, string To)> edges, string format) =>
        format.Equals("dot", StringComparison.OrdinalIgnoreCase)
            ? BuildDotOutput(nodes, edges)
            : BuildJsonOutput(nodes, edges);

    private static string BuildJsonOutput(HashSet<string> nodes, List<(string From, string To)> edges)
    {
        var nodeList = nodes.OrderBy(n => n).ToList();
        var edgeList = edges
            .Select(e => new Dictionary<string, string> { ["from"] = e.From, ["to"] = e.To })
            .ToList();

        return JsonSerializer.Serialize(
            new Dictionary<string, object> { ["nodes"] = nodeList, ["edges"] = edgeList },
            SymbolFormatter.DefaultJsonOptions);
    }

    private static string BuildDotOutput(HashSet<string> nodes, List<(string From, string To)> edges)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph {");

        foreach (var (from, to) in edges)
            sb.AppendLineF($"  \"{from}\" -> \"{to}\";");

        var nodesInEdges = new HashSet<string>(
            edges.SelectMany(e => new[] { e.From, e.To }),
            StringComparer.Ordinal);
        foreach (var node in nodes.Where(n => !nodesInEdges.Contains(n)).OrderBy(n => n))
            sb.AppendLineF($"  \"{node}\";");

        sb.Append('}');
        return sb.ToString();
    }
}
