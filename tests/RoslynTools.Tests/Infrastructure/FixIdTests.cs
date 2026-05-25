using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class FixId_Compute
{
    [Fact]
    public void returns_8_lowercase_hex_characters()
    {
        var id = FixId.Compute("replace-xunit-asserts", "tests/Foo.cs", "Assert.True");

        id.Length.ShouldBe(8);
        id.ShouldMatch("^[0-9a-f]{8}$");
    }

    [Fact]
    public void is_deterministic_for_same_inputs()
    {
        var a = FixId.Compute("replace-xunit-asserts", "tests/Foo.cs", "Assert.True");
        var b = FixId.Compute("replace-xunit-asserts", "tests/Foo.cs", "Assert.True");

        a.ShouldBe(b);
    }

    [Fact]
    public void differs_when_command_differs()
    {
        var a = FixId.Compute("replace-xunit-asserts", "tests/Foo.cs", "Assert.True");
        var b = FixId.Compute("replace-call", "tests/Foo.cs", "Assert.True");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void differs_when_target_differs()
    {
        var a = FixId.Compute("replace-xunit-asserts", "tests/Foo.cs", "Assert.True");
        var b = FixId.Compute("replace-xunit-asserts", "tests/Bar.cs", "Assert.True");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void differs_when_mutation_kind_differs()
    {
        var a = FixId.Compute("replace-xunit-asserts", "tests/Foo.cs", "Assert.True");
        var b = FixId.Compute("replace-xunit-asserts", "tests/Foo.cs", "Assert.Equal");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void does_not_collide_across_field_boundary()
    {
        // If the hash naively concatenated fields, ("ab","c","d") would equal ("a","bc","d").
        // A proper delimiter prevents this.
        var a = FixId.Compute("ab", "c", "d");
        var b = FixId.Compute("a", "bc", "d");

        a.ShouldNotBe(b);
    }
}
