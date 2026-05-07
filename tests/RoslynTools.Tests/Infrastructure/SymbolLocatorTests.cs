using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

public class SymbolLocatorTests
{
    [Fact]
    public async Task Ambiguous_symbol_without_input_reports_the_missing_selection()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count)
                                            {
                                            }

                                            public void Foo(string name)
                                            {
                                            }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var originalIn = Console.In;
            Console.SetIn(new StringReader(""));
            try
            {
                var lookup = SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.Foo");
                var completed = await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(5)));

                Assert.Same(lookup, completed);
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => lookup);
                Assert.Contains("Multiple symbols match 'TestApp.Service.Foo'", ex.Message);
                Assert.Contains("no selection was provided", ex.Message);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }
    }

    [Fact]
    public async Task Type_lookup_prefers_source_declarations_over_referenced_metadata()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Logger.cs", """
                                       public class Logger
                                       {
                                           public Logger()
                                           {
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var symbol = await SymbolLocator.FindSymbolAsync(solution, "Logger", "type");

            Assert.Equal(SymbolKind.NamedType, symbol.Kind);
            Assert.Equal("Logger", symbol.Name);
        }
    }
}
