using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;

namespace RoslynTools.Tests.Navigation;

public class FindCallers : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public FindCallers(RtServicesFixture services) => _services = services;

    [Fact]
    public async Task returns_invocation_sites()
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
            var methodSymbol = await RoslynTools.Core.SymbolLocator.FindSymbolAsync(sln, "Greet", "method");
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
    public async Task returns_nothing_when_no_callers_exist()
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
            var methodSymbol = await RoslynTools.Core.SymbolLocator.FindSymbolAsync(sln, "NeverCalled", "method");
            var callers = await FindInvocationCallersAsync(sln, methodSymbol);

            Assert.Empty(callers);
        }
    }

    [Fact]
    public async Task excludes_nameof_references_from_the_results()
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
            var methodSymbol = await RoslynTools.Core.SymbolLocator.FindSymbolAsync(sln, "Greet", "method");
            var callers = await FindInvocationCallersAsync(sln, methodSymbol);

            Assert.Empty(callers);
        }
    }

    private async Task<List<CallerInfo>> FindInvocationCallersAsync(
        Solution solution,
        ISymbol symbol)
    {
        var referencedSymbols = await SymbolFinder.FindReferencesAsync(symbol, solution);
        var results = new List<CallerInfo>();

        foreach (var referencedSymbol in referencedSymbols)
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

                if (invocation is null || _services.Resolve<FindCallersCommand>().IsNameofExpression(invocation))
                    continue;

                var lineSpan = loc.Location.GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                var containingMethod = invocation.Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Identifier.Text;

                results.Add(new CallerInfo(doc.FilePath, line, containingMethod));
            }

        return results;
    }

    // ---------------------------------------------------------------------------
    // Helper: mirrors the algorithm from FindCallersCommand
    // ---------------------------------------------------------------------------

    private sealed record CallerInfo(string? FilePath, int Line, string? ContainingMethod);
}
