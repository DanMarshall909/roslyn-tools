using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class CircularDepsService : IRtService
{
    internal async Task<IReadOnlyList<IReadOnlyList<string>>> AnalyzeCyclesAsync(Solution sln, string? scope)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var project in sln.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var declaredNamespaces = new HashSet<string>(StringComparer.Ordinal);
                foreach (var nsNode in root.DescendantNodesAndSelf())
                {
                    string? nsName = nsNode switch
                    {
                        NamespaceDeclarationSyntax ns => ns.Name.ToString(),
                        FileScopedNamespaceDeclarationSyntax fsns => fsns.Name.ToString(),
                        _ => null,
                    };
                    if (nsName is not null)
                        declaredNamespaces.Add(nsName);
                }

                var compilationUnit = (CompilationUnitSyntax)root;
                var topLevelUsings = compilationUnit.Usings
                    .Select(u => u.Name?.ToString())
                    .Where(n => n is not null)
                    .Cast<string>()
                    .ToList();

                foreach (var declaredNs in declaredNamespaces)
                {
                    if (!graph.ContainsKey(declaredNs))
                        graph[declaredNs] = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var used in topLevelUsings)
                        AddEdge(graph, declaredNs, used);
                }

                foreach (var nsNode in root.DescendantNodes())
                {
                    string? declaredNs = nsNode switch
                    {
                        NamespaceDeclarationSyntax ns => ns.Name.ToString(),
                        FileScopedNamespaceDeclarationSyntax fsns => fsns.Name.ToString(),
                        _ => null,
                    };
                    if (declaredNs is null)
                        continue;

                    IEnumerable<UsingDirectiveSyntax> usings = nsNode switch
                    {
                        NamespaceDeclarationSyntax ns => ns.Usings,
                        FileScopedNamespaceDeclarationSyntax fsns => fsns.Usings,
                        _ => Enumerable.Empty<UsingDirectiveSyntax>(),
                    };

                    foreach (var u in usings)
                    {
                        var usedNs = u.Name?.ToString();
                        if (usedNs is not null)
                            AddEdge(graph, declaredNs, usedNs);
                    }
                }
            }
        }

        if (scope is not null)
        {
            var filtered = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (node, deps) in graph)
            {
                if (!node.StartsWith(scope, StringComparison.Ordinal))
                    continue;
                filtered[node] = new HashSet<string>(
                    deps.Where(d => d.StartsWith(scope, StringComparison.Ordinal)),
                    StringComparer.Ordinal);
            }
            graph = filtered;
        }

        var cycleStrings = new List<string>();
        var white = new HashSet<string>(graph.Keys, StringComparer.Ordinal);
        var gray = new HashSet<string>(StringComparer.Ordinal);
        var black = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();

        foreach (var node in graph.Keys.OrderBy(n => n))
        {
            if (white.Contains(node))
                DfsCollectStrings(node, graph, white, gray, black, stack, cycleStrings);
        }

        return cycleStrings.Select(s => (IReadOnlyList<string>)s.Split(" -> ")).ToList();
    }

    internal async Task<string> AnalyzeAsync(Solution sln, string? scope)
    {
        var cycles = await AnalyzeCyclesAsync(sln, scope);

        if (cycles.Count == 0)
            return "No circular namespace dependencies found.";

        return string.Join(Environment.NewLine, cycles.Select(nodes => string.Join(" -> ", nodes)));
    }

    private void DfsCollectStrings(
        string node,
        Dictionary<string, HashSet<string>> graph,
        HashSet<string> white,
        HashSet<string> gray,
        HashSet<string> black,
        Stack<string> stack,
        List<string> cycles)
    {
        white.Remove(node);
        gray.Add(node);
        stack.Push(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors.OrderBy(n => n))
            {
                if (black.Contains(neighbor))
                    continue;

                if (gray.Contains(neighbor))
                {
                    var path = stack.Reverse().SkipWhile(n => n != neighbor).ToList();
                    path.Add(neighbor);
                    cycles.Add(string.Join(" -> ", path));
                    continue;
                }

                if (white.Contains(neighbor))
                    DfsCollectStrings(neighbor, graph, white, gray, black, stack, cycles);
            }
        }

        stack.Pop();
        gray.Remove(node);
        black.Add(node);
    }

    private void AddEdge(Dictionary<string, HashSet<string>> graph, string from, string to)
    {
        if (from == to)
            return;
        if (!graph.ContainsKey(from))
            graph[from] = new HashSet<string>(StringComparer.Ordinal);
        graph[from].Add(to);
    }
}
