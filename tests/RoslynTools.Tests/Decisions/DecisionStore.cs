using RoslynTools.Core.Decisions;

namespace RoslynTools.Tests.Decisions;

public class DecisionStore
{
    [Fact]
    public void returns_empty_when_file_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");

        var store = Core.Decisions.DecisionStore.Load(path);

        Assert.False(store.AnyPending);
        Assert.False(store.TryGetAnswer("ambig-Foo-2", out _));
    }

    [Fact]
    public void round_trips_pending_entry_through_save_then_load()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
        try
        {
            var store = Core.Decisions.DecisionStore.Empty();
            store.Add(new DecisionEntry
            {
                Id = "ambig-Foo-2",
                Q = "2 symbols match 'Foo'",
                Options = new Dictionary<int, string>
                {
                    [0] = "Class    Foo  (P1)",
                    [1] = "Method   Bar.Foo()  (P1)"
                }
            });
            store.Save(path);

            var reloaded = Core.Decisions.DecisionStore.Load(path);

            Assert.True(reloaded.AnyPending);
            Assert.Equal(1, reloaded.PendingCount);
            Assert.False(reloaded.TryGetAnswer("ambig-Foo-2", out _));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void returns_choice_when_choose_is_set()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
        try
        {
            File.WriteAllText(path, """
                                    - id: ambig-Foo-2
                                      q: 2 symbols match 'Foo'
                                      options:
                                        0: 'Class    Foo  (P1)'
                                        1: 'Method   Bar.Foo()  (P1)'
                                      choose: 1
                                    """);

            var store = Core.Decisions.DecisionStore.Load(path);

            Assert.False(store.AnyPending);
            Assert.True(store.TryGetAnswer("ambig-Foo-2", out var choice));
            Assert.Equal(1, choice);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void adds_is_idempotent_for_same_id()
    {
        var store = Core.Decisions.DecisionStore.Empty();

        store.Add(new DecisionEntry { Id = "ambig-Foo-2", Q = "first" });
        store.Add(new DecisionEntry { Id = "ambig-Foo-2", Q = "second" });

        Assert.Equal(1, store.PendingCount);
    }

    [Fact]
    public void save_writes_terse_header_comment()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
        try
        {
            var store = Core.Decisions.DecisionStore.Empty();
            store.Add(new DecisionEntry
            {
                Id = "ambig-Foo-2",
                Q = "2 symbols match 'Foo'",
                Options = new Dictionary<int, string> { [0] = "a", [1] = "b" }
            });
            store.Save(path);

            var text = File.ReadAllText(path);

            Assert.StartsWith("# rt decisions", text);
            Assert.Contains("ambig-Foo-2", text);
            Assert.Contains("choose:", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
