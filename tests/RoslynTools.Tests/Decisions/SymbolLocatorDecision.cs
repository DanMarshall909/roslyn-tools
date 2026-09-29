using RoslynTools.Core;
using RoslynTools.Core.Decisions;
using Shouldly;

namespace RoslynTools.Tests.Decisions;

public class SymbolLocatorDecision
{
    [Fact]
    public async Task handles_ambiguous_symbol_with_empty_store_throws_decisions_required_and_records_pending_entry()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count) { }
                                            public void Foo(string name) { }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var store = Core.Decisions.DecisionStore.Empty();
            DecisionContext.Store = store;
            try
            {
                await Assert.ThrowsAsync<DecisionsRequiredException>(() =>
                    SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.Foo"));

                store.AnyPending.ShouldBeTrue();
                store.PendingCount.ShouldBe(1);
                store.TryGetAnswer("ambig-TestApp.Service.Foo-2", out _).ShouldBeFalse();
            }
            finally
            {
                DecisionContext.Store = null;
            }
        }
    }

    [Fact]
    public async Task handles_ambiguous_symbol_with_answered_store_returns_chosen_match()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count) { }
                                            public void Foo(string name) { }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
            try
            {
                File.WriteAllText(path, """
                                        - id: ambig-TestApp.Service.Foo-2
                                          q: 2 symbols match 'TestApp.Service.Foo'
                                          options:
                                            0: 'Method   TestApp.Service.Foo(int count)  (TestProject)'
                                            1: 'Method   TestApp.Service.Foo(string name)  (TestProject)'
                                          choose: 1
                                        """);
                DecisionContext.Store = new Adapters.Decisions.YamlDecisionFileStore().Load(path);

                var symbol = await SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.Foo");

                symbol.ShouldNotBeNull();
                symbol.Name.ShouldBe("Foo");
                Assert.Single(symbol.Parameters);
                symbol.Parameters[0].Type.ToDisplayString().ShouldBe("string");
            }
            finally
            {
                DecisionContext.Store = null;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task out_of_range_choice_throws_with_clear_message()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count) { }
                                            public void Foo(string name) { }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
            try
            {
                File.WriteAllText(path, """
                                        - id: ambig-TestApp.Service.Foo-2
                                          q: 2 symbols match 'TestApp.Service.Foo'
                                          options:
                                            0: 'Method   TestApp.Service.Foo(int count)  (TestProject)'
                                            1: 'Method   TestApp.Service.Foo(string name)  (TestProject)'
                                          choose: 99
                                        """);
                DecisionContext.Store = new Adapters.Decisions.YamlDecisionFileStore().Load(path);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.Foo"));
                ex.Message.ShouldContain("Invalid choice", Case.Sensitive);
                ex.Message.ShouldContain("99", Case.Sensitive);
                ex.Message.ShouldContain("0-1", Case.Sensitive);
            }
            finally
            {
                DecisionContext.Store = null;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task handles_single_match_does_not_invoke_decision_path()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void OnlyFoo() { }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var store = Core.Decisions.DecisionStore.Empty();
            DecisionContext.Store = store;
            try
            {
                var symbol = await SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.OnlyFoo");

                symbol.ShouldNotBeNull();
                symbol.Name.ShouldBe("OnlyFoo");
                store.AnyPending.ShouldBeFalse();
            }
            finally
            {
                DecisionContext.Store = null;
            }
        }
    }
}
