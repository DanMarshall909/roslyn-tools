using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Services for analyzing and managing dependencies in a Roslyn project.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class DependencyGraphService : IRtService
{
    /// <summary>
    ///     Builds a type-level dependency graph and returns it in the requested format.
    /// </summary>
    internal async Task<string> BuildGraphAsync(
        Solution sln,
        string? typeName,
        int? depth,
        string format,
        string? scope)
    {
        var allTypes = await CollectSourceTypesAsync(sln);

        if (typeName != null)
        {
            var root = allTypes.FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.Ordinal) ||
                t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Contains(typeName, StringComparison.Ordinal));

            if (root == null)
                return format.Equals("dot", StringComparison.OrdinalIgnoreCase)
                    ? "digraph { }"
                    : JsonSerializer.Serialize(new { nodes = Array.Empty<string>(), edges = Array.Empty<object>() },
                        SymbolFormatter.DefaultJsonOptions);

            var (targetNodes, targetEdges) = await BuildBfsGraphAsync(sln, root, depth);
            return SerializeGraph(targetNodes, targetEdges, format);
        }

        var typesToGraph = scope is null
            ? allTypes
            : allTypes.Where(t => GetNamespaceName(t).StartsWith(scope, StringComparison.Ordinal)).ToList();
        var bodyDeps = await CollectBodyDependenciesAsync(sln, typesToGraph);
        var (allNodes, allEdges) = BuildFullGraph(typesToGraph, bodyDeps);
        return SerializeGraph(allNodes, allEdges, format);
    }

    private async Task<List<INamedTypeSymbol>> CollectSourceTypesAsync(Solution sln)
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

                foreach (var node in root.DescendantNodesAndSelf().Where(static node =>
                             node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
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

    private (HashSet<string> Nodes, List<(string From, string To)> Edges) BuildFullGraph(
        IEnumerable<INamedTypeSymbol> typesToGraph,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> bodyDeps)
    {
        var (nodes, edgeSet, edges) = CreateStringHashSet();

        foreach (var type in typesToGraph)
        {
            var fromName = GetDisplayName(type);
            nodes.Add(fromName);

            foreach (var dep in GetDependencies(
                         type,
                         bodyDeps.GetValueOrDefault(type) ?? []))
            {
                if (!IsSourceType(dep))
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

    private (HashSet<string> nodes, HashSet<(string, string)> edgeSet, List<(string From, string To)> edges)
        CreateStringHashSet()
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var edgeSet = new HashSet<(string, string)>();
        var edges = new List<(string From, string To)>();

        return (nodes, edgeSet, edges);
    }

    private async Task<(HashSet<string> Nodes, List<(string From, string To)> Edges)> BuildBfsGraphAsync(
        Solution solution,
        INamedTypeSymbol rootType,
        int? maxDepth)
    {
        var (nodes, edgeSet, edges) = CreateStringHashSet();
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

            var bodyDeps = await CollectBodyDependenciesForTypeAsync(solution, current);
            ExpandBfsNode(current, currentDepth, bodyDeps, nodes, edgeSet, edges, visited, queue);
        }

        return (nodes, edges);
    }

    private void ExpandBfsNode(
        INamedTypeSymbol current,
        int currentDepth,
        IReadOnlyList<INamedTypeSymbol> bodyDeps,
        HashSet<string> nodes,
        HashSet<(string, string)> edgeSet,
        List<(string From, string To)> edges,
        HashSet<string> visited,
        Queue<(INamedTypeSymbol Type, int Depth)> queue)
    {
        var fromName = GetDisplayName(current);
        foreach (var dep in GetDependencies(current, bodyDeps))
        {
            if (!IsSourceType(dep))
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

    private IEnumerable<INamedTypeSymbol> GetDependencies(
        INamedTypeSymbol type,
        IReadOnlyList<INamedTypeSymbol> bodyDeps)
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

        foreach (var dep in bodyDeps)
            if (seen.Add(dep.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                yield return dep;
    }

    private async Task<Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>> CollectBodyDependenciesAsync(
        Solution sln,
        IEnumerable<INamedTypeSymbol> sourceTypes)
    {
        var result = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);

        foreach (var type in sourceTypes)
        {
            var deps = await CollectBodyDependenciesForTypeAsync(sln, type);

            if (deps.Count > 0)
                result[type] = deps;
        }

        return result;
    }

    private async Task<List<INamedTypeSymbol>> CollectBodyDependenciesForTypeAsync(
        Solution solution,
        INamedTypeSymbol type)
    {
        var dependencies = new List<INamedTypeSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            foreach (var syntaxRef in method.DeclaringSyntaxReferences)
            {
                var node = await syntaxRef.GetSyntaxAsync();
                if (node is not BaseMethodDeclarationSyntax declaration)
                    continue;

                var body = declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody;
                if (body is null)
                    continue;

                var document = solution.GetDocument(node.SyntaxTree);
                if (document is null)
                    continue;
                var model = await document.GetSemanticModelAsync();
                if (model is null)
                    continue;

                foreach (var name in body.DescendantNodes().OfType<SimpleNameSyntax>())
                    if (model.GetSymbolInfo(name).Symbol is INamedTypeSymbol named &&
                        ExtractNamedType(named) is { } resolved &&
                        seen.Add(resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                        dependencies.Add(resolved);

                foreach (var creation in body.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
                    if (ExtractNamedType(model.GetTypeInfo(creation).Type) is { } resolved &&
                        seen.Add(resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                        dependencies.Add(resolved);
            }

        return dependencies;
    }

    private IEnumerable<INamedTypeSymbol> GetInheritanceDependencies(INamedTypeSymbol type)
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

    private IEnumerable<INamedTypeSymbol> GetFieldDependencies(INamedTypeSymbol type)
    {
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            var dep = ExtractNamedType(field.Type);
            if (dep != null)
                yield return dep;
        }
    }

    private IEnumerable<INamedTypeSymbol> GetMethodDependencies(INamedTypeSymbol type)
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

    private INamedTypeSymbol? ExtractNamedType(ITypeSymbol? type)
    {
        return type switch
        {
            INamedTypeSymbol named => named,
            IArrayTypeSymbol array => ExtractNamedType(array.ElementType),
            _ => null
        };
    }

    private bool IsSourceType(INamedTypeSymbol? type) =>
        type is not null && type.Locations.Any(static location => location.IsInSource);

    private string GetNamespaceName(INamedTypeSymbol type)
    {
        return type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
    }

    private string GetDisplayName(INamedTypeSymbol type)
    {
        var ns = GetNamespaceName(type);
        return string.IsNullOrEmpty(ns) || ns == "<global namespace>"
            ? type.Name
            : type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private string SerializeGraph(HashSet<string> nodes, List<(string From, string To)> edges, string format)
    {
        return format.Equals("dot", StringComparison.OrdinalIgnoreCase)
            ? BuildDotOutput(nodes, edges)
            : BuildJsonOutput(nodes, edges);
    }

    private string BuildJsonOutput(HashSet<string> nodes, List<(string From, string To)> edges)
    {
        var nodeList = nodes.OrderBy(n => n).ToList();
        var edgeList = edges
            .Select(e => new Dictionary<string, string> { ["from"] = e.From, ["to"] = e.To })
            .ToList();

        return JsonSerializer.Serialize(
            new Dictionary<string, object> { ["nodes"] = nodeList, ["edges"] = edgeList },
            SymbolFormatter.DefaultJsonOptions);
    }

    private string BuildDotOutput(HashSet<string> nodes, List<(string From, string To)> edges)
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
