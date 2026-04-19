using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Navigation;

public class FindCallersTests
{
    [Fact]
    public async Task FindCallers_ReturnsInvocationSites()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Greeter.cs", """
            public class Greeter
            {
                public string Greet(string name) => $"Hello, {name}!";
            }
            """);
        project.AddSource("Program.cs", """
            public class Program
            {
                public void Run()
                {
                    var g = new Greeter();
                    g.Greet("Alice");
                    g.Greet("Bob");
                }
                public void Other()
                {
                    var name = nameof(Greeter.Greet); // should NOT be a caller
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var methodSymbol = await SymbolLocator.FindSymbolAsync(sln, "Greet", "method");
            var callers = await FindInvocationCallersAsync(sln, methodSymbol);

            Assert.Equal(2, callers.Count);
            Assert.All(callers, c =>
            {
                Assert.NotNull(c.FilePath);
                Assert.True(c.Line > 0);
                Assert.Equal("Run", c.ContainingMethod);
            });
        }
    }

    [Fact]
    public async Task FindCallers_ZeroCallers_ReturnsEmpty()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            public class Service
            {
                public void NeverCalled() { }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var methodSymbol = await SymbolLocator.FindSymbolAsync(sln, "NeverCalled", "method");
            var callers = await FindInvocationCallersAsync(sln, methodSymbol);

            Assert.Empty(callers);
        }
    }

    [Fact]
    public async Task FindCallers_ExcludesNameofReferences()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Greeter.cs", """
            public class Greeter
            {
                public string Greet(string name) => $"Hello, {name}!";
            }
            """);
        project.AddSource("Program.cs", """
            public class Program
            {
                public void Other()
                {
                    var name = nameof(Greeter.Greet); // nameof reference only
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var methodSymbol = await SymbolLocator.FindSymbolAsync(sln, "Greet", "method");
            var callers = await FindInvocationCallersAsync(sln, methodSymbol);

            Assert.Empty(callers);
        }
    }

    // ---------------------------------------------------------------------------
    // Helper: mirrors the algorithm from FindCallersCommand
    // ---------------------------------------------------------------------------

    private record CallerInfo(string? FilePath, int Line, string? ContainingMethod);

    private static async Task<List<CallerInfo>> FindInvocationCallersAsync(
        Solution solution,
        ISymbol symbol)
    {
        var referencedSymbols = await SymbolFinder.FindReferencesAsync(symbol, solution);
        var results = new List<CallerInfo>();

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var loc in referencedSymbol.Locations)
            {
                var doc = loc.Document;
                var root = await doc.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var node = root.FindNode(loc.Location.SourceSpan);

                // Only include actual invocations (not nameof, typeof, etc.)
                var invocation = node.AncestorsAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault();

                if (invocation is null || FindCallersCommand.IsNameofExpression(invocation))
                    continue;

                var lineSpan = loc.Location.GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                var containingMethod = invocation.Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Identifier.Text;

                results.Add(new CallerInfo(doc.FilePath, line, containingMethod));
            }
        }

        return results;
    }
}
