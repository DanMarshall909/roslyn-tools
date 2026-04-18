using System.CommandLine;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class DependencyGraphCommand : IRtCommand
{
    private DependencyGraphCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "dependency-graph",
        Mutates: false,
        JsonOutput: true,
        Category: "Analysis",
        Aliases: ["depgraph"],
        WhenToUse: "Visualising the type-level dependency graph for a solution or a specific root type, to understand coupling before a large refactor. Use --format dot to pipe into Graphviz, or --format json for programmatic consumption. Use --type + --depth to focus on a single type's fan-out.",
        Examples:
        [
            "rt dependency-graph --solution C:\\code\\libraries\\ProjectX.Libraries.sln --scope ProjectX.Core",
            "rt depgraph --type BookingService --depth 2 --solution RoslynTools.slnx",
            "rt dependency-graph --format dot --solution RoslynTools.slnx | dot -Tsvg -o deps.svg",
        ],
        KnownIssues:
        [
            "Invoking via `cmd //c` from bash on Windows crashes during workspace load — see ai-improvements row #13.",
            "Graph edges are built from fields, method parameters/returns, base types, and interfaces only — local variable type usages inside method bodies are missed. Static command classes with no fields appear as isolated nodes even when they reference WorkspaceLoader/SolutionWriter internally. See ai-improvements row #12.",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string?>("--type", "Root type to start graph from (optional)");
        var depthOpt = new Option<int?>("--depth", "Maximum traversal depth (optional)");
        var formatOpt = new Option<string?>("--format", "Output format: json (default) or dot");
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");

        var cmd = new Command("dependency-graph", "Build a type-level dependency graph for the solution");
        cmd.AddAlias("depgraph");
        cmd.Add(typeOpt);
        cmd.Add(depthOpt);
        cmd.Add(formatOpt);
        cmd.Add(scopeOpt);
        cmd.Add(solutionOpt);

        cmd.SetHandler(async (typeName, depth, format, scope, solution) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var result = await BuildGraphAsync(sln, typeName, depth, format ?? "json", scope);
                Console.WriteLine(result);
            }
        }, typeOpt, depthOpt, formatOpt, scopeOpt, solutionOpt);

        return cmd;
    }

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
        // Collect all source-defined named types from the solution
        var allTypes = await CollectSourceTypesAsync(sln);

        // Build the set of source-type FQNs for filtering non-solution types
        var sourceTypeFqns = new HashSet<string>(
            allTypes.Select(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
            StringComparer.Ordinal);

        // Also index by metadata name for fast lookup
        var sourceTypeByName = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var t in allTypes)
        {
            var name = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            sourceTypeByName.TryAdd(name, t);
        }

        // Apply scope filter (by namespace prefix)
        IEnumerable<INamedTypeSymbol> typesToGraph = allTypes;
        if (scope != null)
            typesToGraph = allTypes.Where(t => GetNamespaceName(t).StartsWith(scope, StringComparison.Ordinal));

        // If --type specified, find that type and do BFS; otherwise graph everything
        if (typeName != null)
        {
            var root = allTypes.FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.Ordinal) ||
                t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Contains(typeName, StringComparison.Ordinal));

            if (root == null)
                return format.Equals("dot", StringComparison.OrdinalIgnoreCase)
                    ? "digraph { }"
                    : JsonSerializer.Serialize(new { nodes = Array.Empty<string>(), edges = Array.Empty<object>() }, SymbolFormatter.DefaultJsonOptions);

            // BFS up to depth
            var (nodes, edges) = BuildBfsGraph(root, depth, allTypes, sourceTypeByName);
            return SerializeGraph(nodes, edges, format);
        }
        else
        {
            // Build full graph for all in-scope types
            var (nodes, edges) = BuildFullGraph(typesToGraph, allTypes, sourceTypeByName);
            return SerializeGraph(nodes, edges, format);
        }
    }

    private static async Task<List<INamedTypeSymbol>> CollectSourceTypesAsync(Solution sln)
    {
        var result = new List<INamedTypeSymbol>();
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                continue;

            foreach (var document in project.Documents)
            {
                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null)
                    continue;

                var root = await document.GetSyntaxRootAsync();
                if (root == null)
                    continue;

                // Find all type declaration nodes and get their declared symbols
                foreach (var node in root.DescendantNodesAndSelf())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(node) as INamedTypeSymbol;
                    if (symbol == null)
                        continue;
                    if (symbol.IsImplicitlyDeclared)
                        continue;
                    if (symbol.TypeKind == TypeKind.Error)
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
        Dictionary<string, INamedTypeSymbol> sourceTypeByName)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var edgeSet = new HashSet<(string, string)>();
        var edges = new List<(string From, string To)>();

        foreach (var type in typesToGraph)
        {
            var fromName = GetDisplayName(type);
            nodes.Add(fromName);

            foreach (var dep in GetDependencies(type))
            {
                // Only include types that are in the solution source
                if (!IsSourceType(dep, sourceTypeByName))
                    continue;

                var toName = GetDisplayName(dep);
                if (fromName == toName)
                    continue;

                nodes.Add(toName);
                var edge = (fromName, toName);
                if (edgeSet.Add(edge))
                    edges.Add(edge);
            }
        }

        return (nodes, edges);
    }

    private static (HashSet<string> Nodes, List<(string From, string To)> Edges) BuildBfsGraph(
        INamedTypeSymbol rootType,
        int? maxDepth,
        List<INamedTypeSymbol> allTypes,
        Dictionary<string, INamedTypeSymbol> sourceTypeByName)
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

            ExpandBfsNode(current, currentDepth, sourceTypeByName, nodes, edgeSet, edges, visited, queue);
        }

        return (nodes, edges);
    }

    private static void ExpandBfsNode(
        INamedTypeSymbol current,
        int currentDepth,
        Dictionary<string, INamedTypeSymbol> sourceTypeByName,
        HashSet<string> nodes,
        HashSet<(string, string)> edgeSet,
        List<(string From, string To)> edges,
        HashSet<string> visited,
        Queue<(INamedTypeSymbol Type, int Depth)> queue)
    {
        var fromName = GetDisplayName(current);
        foreach (var dep in GetDependencies(current))
        {
            if (!IsSourceType(dep, sourceTypeByName))
                continue;

            var toName = GetDisplayName(dep);
            if (fromName == toName)
                continue;

            var edge = (fromName, toName);
            if (edgeSet.Add(edge))
                edges.Add(edge);

            if (nodes.Add(toName) && !visited.Contains(toName))
            {
                visited.Add(toName);
                queue.Enqueue((dep, currentDepth + 1));
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetDependencies(INamedTypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dep in GetInheritanceDependencies(type, seen))
            yield return dep;

        foreach (var dep in GetFieldDependencies(type, seen))
            yield return dep;

        foreach (var dep in GetMethodDependencies(type, seen))
            yield return dep;
    }

    private static IEnumerable<INamedTypeSymbol> GetInheritanceDependencies(INamedTypeSymbol type, HashSet<string> seen)
    {
        if (type.BaseType != null && type.BaseType.SpecialType == SpecialType.None)
        {
            var unwrapped = Unwrap(type.BaseType, seen);
            if (unwrapped != null)
                yield return unwrapped;
        }

        foreach (var iface in type.Interfaces)
        {
            var unwrapped = Unwrap(iface, seen);
            if (unwrapped != null)
                yield return unwrapped;
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetFieldDependencies(INamedTypeSymbol type, HashSet<string> seen)
    {
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            var dep = ExtractNamedType(field.Type);
            if (dep != null)
            {
                var unwrapped = Unwrap(dep, seen);
                if (unwrapped != null)
                    yield return unwrapped;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetMethodDependencies(INamedTypeSymbol type, HashSet<string> seen)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            var retDep = ExtractNamedType(method.ReturnType);
            if (retDep != null)
            {
                var unwrapped = Unwrap(retDep, seen);
                if (unwrapped != null)
                    yield return unwrapped;
            }

            foreach (var param in method.Parameters)
            {
                var paramDep = ExtractNamedType(param.Type);
                if (paramDep != null)
                {
                    var unwrapped = Unwrap(paramDep, seen);
                    if (unwrapped != null)
                        yield return unwrapped;
                }
            }
        }
    }

    private static INamedTypeSymbol? Unwrap(INamedTypeSymbol? symbol, HashSet<string> seen)
    {
        if (symbol == null)
            return null;
        var key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!seen.Add(key))
            return null;
        return symbol;
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
        if (type == null)
            return false;

        // Must have at least one source location
        if (!type.Locations.Any(l => l.IsInSource))
            return false;

        // Exclude System.* and Microsoft.* unless defined in source
        var ns = GetNamespaceName(type);
        if (ns.StartsWith("System.", StringComparison.Ordinal) || ns == "System" ||
            ns.StartsWith("Microsoft.", StringComparison.Ordinal) || ns == "Microsoft")
        {
            // Still allow if it's in source (e.g., custom type named in System namespace)
            return type.Locations.Any(l => l.IsInSource);
        }

        return true;
    }

    private static string GetNamespaceName(INamedTypeSymbol type)
    {
        return type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
    }

    private static string GetDisplayName(INamedTypeSymbol type)
    {
        // Use short name when no namespace, otherwise use metadata name
        var ns = GetNamespaceName(type);
        return string.IsNullOrEmpty(ns) || ns == "<global namespace>"
            ? type.Name
            : type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static string SerializeGraph(HashSet<string> nodes, List<(string From, string To)> edges, string format)
    {
        if (format.Equals("dot", StringComparison.OrdinalIgnoreCase))
            return BuildDotOutput(nodes, edges);

        return BuildJsonOutput(nodes, edges);
    }

    private static string BuildJsonOutput(HashSet<string> nodes, List<(string From, string To)> edges)
    {
        var nodeList = nodes.OrderBy(n => n).ToList();
        var edgeList = edges
            .Select(e => new Dictionary<string, string> { ["from"] = e.From, ["to"] = e.To })
            .ToList();

        var graph = new Dictionary<string, object>
        {
            ["nodes"] = nodeList,
            ["edges"] = edgeList,
        };

        return JsonSerializer.Serialize(graph, SymbolFormatter.DefaultJsonOptions);
    }

    private static string BuildDotOutput(HashSet<string> nodes, List<(string From, string To)> edges)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph {");

        foreach (var (from, to) in edges)
            sb.AppendLine($"  \"{from}\" -> \"{to}\";");

        // Nodes with no edges still need to appear
        var nodesInEdges = new HashSet<string>(
            edges.SelectMany(e => new[] { e.From, e.To }),
            StringComparer.Ordinal);
        foreach (var node in nodes.Where(n => !nodesInEdges.Contains(n)).OrderBy(n => n))
            sb.AppendLine($"  \"{node}\";");

        sb.Append('}');
        return sb.ToString();
    }
}
