using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class SymbolLocator
{
    [Fact]
    public async Task handles_ambiguous_symbol_without_input_reports_the_missing_selection()
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
                var lookup = Core.SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.Foo");
                var completed = await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(5)));

                completed.ShouldBeSameAs(lookup);
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => lookup);
                ex.Message.ShouldContain("Multiple symbols match 'TestApp.Service.Foo'", Case.Sensitive);
                ex.Message.ShouldContain("no selection was provided", Case.Sensitive);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }
    }

    [Fact]
    public async Task handles_type_lookup_prefers_source_declarations_over_referenced_metadata()
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
            var symbol = await Core.SymbolLocator.FindSymbolAsync(solution, "Logger", "type");

            symbol.Kind.ShouldBe(SymbolKind.NamedType);
            symbol.Name.ShouldBe("Logger");
        }
    }
}
