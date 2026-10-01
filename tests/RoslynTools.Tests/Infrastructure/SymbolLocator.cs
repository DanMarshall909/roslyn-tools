using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class SymbolLocator
{
    [Fact]
    public async Task many_legacy_symbol_matches_return_bounded_candidate_evidence()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Workers.cs", string.Join(Environment.NewLine,
            Enumerable.Range(0, 80).Select(index =>
                $"namespace N{index:D3} {{ public sealed class Worker {{ public void ExecuteAsync() {{ }} }} }}")));
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var result = await Core.SymbolLocator.TryFindSymbolAsync(
                solution, "ExecuteAsync", "method");
            var error = result.Match<RtError?>(_ => null, failure => failure);

            error.ShouldNotBeNull();
            error.Kind.ShouldBe(RtErrorKind.AmbiguousMatch);
            error.Context!["candidateCount"].ShouldBe("80");
            var displayed = int.Parse(error.Context["displayedCandidateCount"],
                CultureInfo.InvariantCulture);
            displayed.ShouldBeInRange(1, 12);
            error.Context["candidatesTruncated"].ShouldBe("true");
            error.Context["candidates"].Split(';').Length.ShouldBe(displayed);
            error.Message.ShouldContain("Pass a more specific fully-qualified name");
            Encoding.UTF8.GetByteCount(error.Message).ShouldBeLessThanOrEqualTo(2_048);
            Encoding.UTF8.GetByteCount(error.Context["candidates"])
                .ShouldBeLessThanOrEqualTo(4_096);
        }
    }

    [Fact]
    public async Task long_legacy_symbol_name_keeps_ambiguity_error_within_byte_budgets()
    {
        var name = new string('界', 500);
        using var project = new AdhocProjectBuilder();
        project.AddSource("LongNames.cs", $$"""
            namespace First { public sealed class Service { public void {{name}}() { } } }
            namespace Second { public sealed class Service { public void {{name}}() { } } }
            """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var result = await Core.SymbolLocator.TryFindSymbolAsync(solution, name, "method");
            var error = result.Match<RtError?>(_ => null, failure => failure);

            error.ShouldNotBeNull();
            error.Kind.ShouldBe(RtErrorKind.AmbiguousMatch);
            error.Context!["candidateCount"].ShouldBe("2");
            error.Context["displayedCandidateCount"].ShouldBe("0");
            error.Context["candidatesTruncated"].ShouldBe("true");
            error.Context["candidates"].ShouldBe(string.Empty);
            error.Context["symbolTruncated"].ShouldBe("true");
            error.Context["symbol"].ShouldEndWith("…");
            Encoding.UTF8.GetByteCount(error.Context["symbol"]).ShouldBeLessThanOrEqualTo(256);
            Encoding.UTF8.GetByteCount(error.Message).ShouldBeLessThanOrEqualTo(2_048);
        }
    }

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
                ex.Message.ShouldContain("Pass a more specific fully-qualified name", Case.Sensitive);
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

    [Fact]
    public async Task try_find_by_fqn_returns_ok_for_a_unique_match()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public class Widget
                                       {
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var result = await Core.SymbolLocator.TryFindSymbolByFqnAsync(solution, "TestApp.Widget", "type");

            result.IsOk.ShouldBeTrue();
            result.Match(s => s.Name, _ => "err").ShouldBe("Widget");
        }
    }

    [Fact]
    public async Task try_find_by_fqn_returns_err_not_found_with_preserved_message()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public class Widget
                                       {
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var result = await Core.SymbolLocator.TryFindSymbolByFqnAsync(solution, "TestApp.Missing", "type");

            result.IsErr.ShouldBeTrue();
            result.Match(_ => default!, e => e).Kind.ShouldBe(RtErrorKind.NotFound);
            result.Match(_ => "ok", e => e.Message)
                .ShouldBe("No symbol with FQN 'TestApp.Missing' found in solution.");
        }
    }

    [Fact]
    public async Task try_find_by_fqn_returns_err_ambiguous_listing_candidate_fqns()
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
                var lookup = Core.SymbolLocator.TryFindSymbolByFqnAsync(solution, "TestApp.Service.Foo", null);
                var completed = await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(5)));
                completed.ShouldBeSameAs(lookup);

                var result = await lookup;
                result.IsErr.ShouldBeTrue();
                var error = result.Match(_ => default!, e => e);
                error.Kind.ShouldBe(RtErrorKind.AmbiguousMatch);
                error.Message.ShouldContain("TestApp.Service.Foo(int)", Case.Sensitive);
                error.Message.ShouldContain("TestApp.Service.Foo(string)", Case.Sensitive);
                error.Context.ShouldNotBeNull();
                error.Context!["candidates"].ShouldContain("TestApp.Service.Foo(int)", Case.Sensitive);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }
    }

    [Fact]
    public async Task try_find_type_by_fqn_returns_err_not_found_with_preserved_message()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public class Widget
                                       {
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var result = await Core.SymbolLocator.TryFindTypeByFqnAsync(solution, "TestApp.Missing");

            result.IsErr.ShouldBeTrue();
            result.Match(_ => default!, e => e).Kind.ShouldBe(RtErrorKind.NotFound);
            result.Match(_ => "ok", e => e.Message)
                .ShouldBe("No type with FQN 'TestApp.Missing' found in solution.");
        }
    }

    [Fact]
    public async Task try_find_method_by_spec_returns_err_not_found_with_preserved_message()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Bar()
                                            {
                                            }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var result = await Core.SymbolLocator.TryFindMethodBySpecAsync(solution, "TestApp.Service.Missing");

            result.IsErr.ShouldBeTrue();
            result.Match(_ => default!, e => e).Kind.ShouldBe(RtErrorKind.NotFound);
            result.Match(_ => "ok", e => e.Message)
                .ShouldBe("Method 'TestApp.Service.Missing' not found in solution.");
        }
    }

    [Fact]
    public async Task bridge_find_by_fqn_still_throws_preserved_not_found_message()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public class Widget
                                       {
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Core.SymbolLocator.FindSymbolByFqnAsync(solution, "TestApp.Missing", "type"));
            ex.Message.ShouldBe("No symbol with FQN 'TestApp.Missing' found in solution.");
        }
    }
}
