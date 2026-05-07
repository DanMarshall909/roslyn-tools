using RoslynTools.Core;
using RoslynTools.Core.Decisions;

namespace RoslynTools.Tests.Decisions;

public class SymbolLocatorDecisionTests
{
    [Fact]
    public async Task Ambiguous_symbol_with_empty_store_throws_DecisionsRequired_and_records_pending_entry()
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
            var store = DecisionStore.Empty();
            DecisionContext.Store = store;
            try
            {
                await Assert.ThrowsAsync<DecisionsRequiredException>(() =>
                    SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.Foo"));

                Assert.True(store.AnyPending);
                Assert.Equal(1, store.PendingCount);
                Assert.False(store.TryGetAnswer("ambig-TestApp.Service.Foo-2", out _));
            }
            finally
            {
                DecisionContext.Store = null;
            }
        }
    }

    [Fact]
    public async Task Ambiguous_symbol_with_answered_store_returns_chosen_match()
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
                DecisionContext.Store = DecisionStore.Load(path);

                var symbol = await SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.Foo");

                Assert.NotNull(symbol);
                Assert.Equal("Foo", symbol.Name);
                Assert.Single(symbol.Parameters);
                Assert.Equal("string", symbol.Parameters[0].Type.ToDisplayString());
            }
            finally
            {
                DecisionContext.Store = null;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Out_of_range_choice_throws_with_clear_message()
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
                DecisionContext.Store = DecisionStore.Load(path);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.Foo"));
                Assert.Contains("Invalid choice", ex.Message);
                Assert.Contains("99", ex.Message);
                Assert.Contains("0-1", ex.Message);
            }
            finally
            {
                DecisionContext.Store = null;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Single_match_does_not_invoke_decision_path()
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
            var store = DecisionStore.Empty();
            DecisionContext.Store = store;
            try
            {
                var symbol = await SymbolLocator.FindMethodBySpecAsync(solution, "TestApp.Service.OnlyFoo");

                Assert.NotNull(symbol);
                Assert.Equal("OnlyFoo", symbol.Name);
                Assert.False(store.AnyPending);
            }
            finally
            {
                DecisionContext.Store = null;
            }
        }
    }
}
