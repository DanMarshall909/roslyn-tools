using RoslynTools.Core.Decisions;
using Shouldly;

namespace RoslynTools.Tests.Decisions;

public class DecisionStore
{
    [Fact]
    public void returns_empty_when_file_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");

        var store = new Adapters.Decisions.YamlDecisionFileStore().Load(path);

        store.AnyPending.ShouldBeFalse();
        store.TryGetAnswer("ambig-Foo-2", out _).ShouldBeFalse();
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
            new Adapters.Decisions.YamlDecisionFileStore().Save(path, store);

            var reloaded = new Adapters.Decisions.YamlDecisionFileStore().Load(path);

            reloaded.AnyPending.ShouldBeTrue();
            reloaded.PendingCount.ShouldBe(1);
            reloaded.TryGetAnswer("ambig-Foo-2", out _).ShouldBeFalse();
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

            var store = new Adapters.Decisions.YamlDecisionFileStore().Load(path);

            store.AnyPending.ShouldBeFalse();
            store.TryGetAnswer("ambig-Foo-2", out var choice).ShouldBeTrue();
            choice.ShouldBe(1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t")]
    [InlineData("null")]
    public void empty_decision_documents_have_no_pending_choices(string yaml)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, yaml);
            var store = new Adapters.Decisions.YamlDecisionFileStore().Load(path);
            store.Entries.ShouldBeEmpty();
            store.TryGetAnswer("missing", out var choice).ShouldBeFalse();
            choice.ShouldBe(-1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void load_preserves_entry_order_answers_and_unknown_property_compatibility()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                - id: answered
                  q: First
                  choose: 0
                  future-property: ignored
                - id: pending
                  q: Second
                """);
            var files = new Adapters.Decisions.YamlDecisionFileStore();
            var store = files.Load(path);
            store.Add(new DecisionEntry { Id = "answered", Q = "Replacement", Choose = 1 });
            files.Save(path, store);
            var reloaded = files.Load(path);

            reloaded.Entries.Select(entry => entry.Id).ShouldBe(["answered", "pending"]);
            reloaded.Entries[0].Q.ShouldBe("First");
            reloaded.PendingCount.ShouldBe(1);
            reloaded.TryGetAnswer("answered", out var choice).ShouldBeTrue();
            choice.ShouldBe(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void malformed_yaml_is_reported_without_replacing_the_file()
    {
        var path = Path.GetTempFileName();
        const string malformed = "- id: [unterminated";
        try
        {
            File.WriteAllText(path, malformed);
            Should.Throw<YamlDotNet.Core.YamlException>(() => new Adapters.Decisions.YamlDecisionFileStore().Load(path));
            File.ReadAllText(path).ShouldBe(malformed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void adds_is_idempotent_for_same_id()
    {
        var store = Core.Decisions.DecisionStore.Empty();

        store.Add(new DecisionEntry { Id = "ambig-Foo-2", Q = "first" });
        store.Add(new DecisionEntry { Id = "ambig-Foo-2", Q = "second" });

        store.PendingCount.ShouldBe(1);
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
            new Adapters.Decisions.YamlDecisionFileStore().Save(path, store);

            var text = File.ReadAllText(path);

            text.ShouldStartWith("# rt decisions — fill `choose:` for each entry, then re-run the same command." + Environment.NewLine
                + "# `choose:` is the integer index of the option you want." + Environment.NewLine, Case.Sensitive);
            text.ShouldContain("ambig-Foo-2", Case.Sensitive);
            text.ShouldContain("choose:", Case.Sensitive);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
