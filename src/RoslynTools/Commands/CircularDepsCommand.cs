using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

internal sealed class CircularDepsCommand : IRtCommand
{
    private CircularDepsCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "circular-deps",
        Mutates: false,
        JsonOutput: false,
        Category: "Analysis",
        Aliases: ["cdeps"],
        WhenToUse: "Detecting circular namespace dependencies in a solution before a refactor, or after adding new inter-namespace references. Use --scope to limit analysis to your domain namespaces and ignore third-party noise.",
        Examples:
        [
            "rt circular-deps --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt cdeps --scope ProjectX.Core --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt circular-deps --solution RoslynTools.slnx --scope RoslynTools",
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
    {
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");

        var cmd = new Command("circular-deps", "Detect circular namespace/type dependencies in the solution");
        cmd.AddAlias("cdeps");
        cmd.Add(scopeOpt);
        cmd.Add(solutionOpt);

        cmd.SetHandler(async (scope, solution) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var cycles = await AnalyzeCyclesAsync(sln, scope);
                var result = CircularDepsResult.FromCycles(cycles);
                RtSerializer.Write(result);
            }
        }, scopeOpt, solutionOpt);

        return cmd;
    }

    /// <summary>
    /// Analyzes the solution for circular namespace dependencies.
    /// Returns a structured list of cycles, where each cycle is an ordered list of namespace nodes.
    /// </summary>
    internal static async Task<IReadOnlyList<IReadOnlyList<string>>> AnalyzeCyclesAsync(Solution sln, string? scope)
    {
        // Build adjacency list: namespace -> set of namespaces it depends on
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var project in sln.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                // Collect namespaces declared in this file
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

                // Collect top-level using directives (outside any namespace)
                var compilationUnit = (CompilationUnitSyntax)root;
                var topLevelUsings = compilationUnit.Usings
                    .Select(u => u.Name?.ToString())
                    .Where(n => n is not null)
                    .Cast<string>()
                    .ToList();

                // For each declared namespace, add edges to used namespaces
                foreach (var declaredNs in declaredNamespaces)
                {
                    if (!graph.ContainsKey(declaredNs))
                        graph[declaredNs] = new HashSet<string>(StringComparer.Ordinal);

                    // Add edges from top-level usings
                    foreach (var used in topLevelUsings)
                        AddEdge(graph, declaredNs, used);
                }

                // Collect usings inside each namespace block
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

        // Apply scope filter: keep only nodes (and edges) starting with the given prefix
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

        // DFS cycle detection using white/gray/black coloring
        var cycleStrings = new List<string>();
        var white = new HashSet<string>(graph.Keys, StringComparer.Ordinal); // unvisited
        var gray = new HashSet<string>(StringComparer.Ordinal);              // in current path
        var black = new HashSet<string>(StringComparer.Ordinal);             // fully processed
        var stack = new Stack<string>();

        foreach (var node in graph.Keys.OrderBy(n => n))
        {
            if (white.Contains(node))
                DfsCollectStrings(node, graph, white, gray, black, stack, cycleStrings);
        }

        return cycleStrings.Select(s => (IReadOnlyList<string>)s.Split(" -> ")).ToList();
    }

    /// <summary>
    /// Analyzes the solution for circular namespace dependencies.
    /// Returns a human-readable string describing any cycles found.
    /// </summary>
    internal static async Task<string> AnalyzeAsync(Solution sln, string? scope)
    {
        var cycles = await AnalyzeCyclesAsync(sln, scope);

        if (cycles.Count == 0)
            return "No circular namespace dependencies found.";

        return string.Join(Environment.NewLine, cycles.Select(nodes => string.Join(" -> ", nodes)));
    }

    private static void DfsCollectStrings(
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
                    // Found a cycle — reconstruct path from 'neighbor' to current node
                    var path = stack.Reverse().SkipWhile(n => n != neighbor).ToList();
                    path.Add(neighbor); // close the cycle
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

    private static void AddEdge(Dictionary<string, HashSet<string>> graph, string from, string to)
    {
        if (from == to)
            return;
        if (!graph.ContainsKey(from))
            graph[from] = new HashSet<string>(StringComparer.Ordinal);
        graph[from].Add(to);
    }
}
