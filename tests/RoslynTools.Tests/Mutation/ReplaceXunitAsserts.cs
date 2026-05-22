using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ReplaceXunitAsserts : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ReplaceXunitAsserts(RtServicesFixture services) => _services = services;

    [Fact]
    public async Task converts_equality_assertions_to_fluent_assertions()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     var actual = 42;
                                                     Assert.Equal(42, actual);
                                                     Xunit.Assert.Equal(42, actual);
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var exitCode = await App.RunAsync(["replace-xunit-asserts", "--solution", "ignored", "--apply"]);

            exitCode.ShouldBe(0);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldContain("using Shouldly;", Case.Sensitive);
        content.ShouldContain("actual.ShouldBe(42);", Case.Sensitive);
        CountOccurrences(content, "actual.ShouldBe(42);").ShouldBe(2);
        content.ShouldNotContain("Assert.Equal", Case.Sensitive);
    }

    [Fact]
    public async Task converts_common_assertions_to_fluent_assertions()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     var actual = "prefix-value";
                                                     var other = new object();
                                                     Assert.NotEqual("wrong", actual);
                                                     Assert.Same(other, other);
                                                     Assert.NotSame(new object(), other);
                                                     Assert.True(actual.Length > 0);
                                                     Assert.False(actual.Length == 0);
                                                     object? maybe = null;
                                                     Assert.Null(maybe);
                                                     Assert.NotNull(actual);
                                                     Assert.Empty(Array.Empty<string>());
                                                     Assert.NotEmpty(new[] { "value" });
                                                     Assert.Contains("value", actual);
                                                     Assert.DoesNotContain("missing", actual);
                                                     Assert.StartsWith("prefix", actual);
                                                     Assert.EndsWith("value", actual);
                                                     Assert.IsType<string>(actual);
                                                     Assert.IsAssignableFrom<object>(actual);
                                                     Assert.Throws<InvalidOperationException>(() => throw new InvalidOperationException());
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false, true);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldContain("actual.ShouldNotBe(\"wrong\");", Case.Sensitive);
        content.ShouldContain("other.ShouldBeSameAs(other);", Case.Sensitive);
        content.ShouldContain("other.ShouldNotBeSameAs(new object());", Case.Sensitive);
        content.ShouldContain("(actual.Length > 0).ShouldBeTrue();", Case.Sensitive);
        content.ShouldContain("(actual.Length == 0).ShouldBeFalse();", Case.Sensitive);
        content.ShouldContain("maybe.ShouldBeNull();", Case.Sensitive);
        content.ShouldContain("actual.ShouldNotBeNull();", Case.Sensitive);
        content.ShouldContain("Array.Empty<string>().ShouldBeEmpty();", Case.Sensitive);
        content.ShouldContain("new[] { \"value\" }.ShouldNotBeEmpty();", Case.Sensitive);
        content.ShouldContain("actual.ShouldContain(\"value\", Case.Sensitive);", Case.Sensitive);
        content.ShouldContain("actual.ShouldNotContain(\"missing\", Case.Sensitive);", Case.Sensitive);
        content.ShouldContain("actual.ShouldStartWith(\"prefix\", Case.Sensitive);", Case.Sensitive);
        content.ShouldContain("actual.ShouldEndWith(\"value\", Case.Sensitive);", Case.Sensitive);
        content.ShouldContain("actual.ShouldBeOfType<string>();", Case.Sensitive);
        content.ShouldContain("actual.ShouldBeAssignableTo<object>();", Case.Sensitive);
        content.ShouldContain("Should.Throw<InvalidOperationException>(() => throw new InvalidOperationException());", Case.Sensitive);
    }

    [Fact]
    public async Task converts_contains_collection_predicate_overload()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using System.Collections.Generic;
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     var list = new List<string> { "x" };
                                                     Assert.Contains(list, s => s == "x");
                                                     Assert.DoesNotContain(list, s => s == "z");
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false, true);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldContain("list.ShouldContain(s => s == \"x\");", Case.Sensitive);
        content.ShouldContain("list.ShouldNotContain(s => s == \"z\");", Case.Sensitive);
    }

    [Fact]
    public async Task converts_nullable_bool_assertions_to_should_be_value()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     bool? maybe = true;
                                                     Assert.True(maybe);
                                                     Assert.False(maybe);
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false, true);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldContain("maybe.ShouldBe(true);", Case.Sensitive);
        content.ShouldContain("maybe.ShouldBe(false);", Case.Sensitive);
        content.ShouldNotContain("maybe.ShouldBeTrue();", Case.Sensitive);
        content.ShouldNotContain("maybe.ShouldBeFalse();", Case.Sensitive);
    }

    [Fact]
    public async Task adds_null_forgiving_when_assignable_from_result_is_used()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using System.Collections.Generic;
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     object o = new List<string>();
                                                     var captured = Assert.IsAssignableFrom<List<string>>(o);
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false, true);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldContain("var captured = o.ShouldBeAssignableTo<List<string>>()!;", Case.Sensitive);
    }

    [Fact]
    public async Task wraps_nullable_string_receiver_with_null_forgiving()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     var maybe = GetMaybe();
                                                     Assert.Contains("a", maybe);
                                                 }

                                                 private static string? GetMaybe() => "abc";
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false, true);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldContain("maybe!.ShouldContain(\"a\", Case.Sensitive);", Case.Sensitive);
    }

    [Fact]
    public async Task leaves_contains_with_conditional_access_predicate_unchanged()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using System.Collections.Generic;
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     var list = new List<string?> { "x" };
                                                     Assert.Contains(list, s => s?.Length == 1);
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false, true);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldContain("Assert.Contains(list, s => s?.Length == 1);", Case.Sensitive);
        content.ShouldNotContain("ShouldContain", Case.Sensitive);
    }

    [Fact]
    public async Task uses_case_sensitive_for_string_substring_comparisons()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     var s = "Format";
                                                     Assert.Contains("F", s);
                                                     Assert.DoesNotContain("X", s);
                                                     Assert.StartsWith("F", s);
                                                     Assert.EndsWith("t", s);
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false, true);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldContain("s.ShouldContain(\"F\", Case.Sensitive);", Case.Sensitive);
        content.ShouldContain("s.ShouldNotContain(\"X\", Case.Sensitive);", Case.Sensitive);
        content.ShouldContain("s.ShouldStartWith(\"F\", Case.Sensitive);", Case.Sensitive);
        content.ShouldContain("s.ShouldEndWith(\"t\", Case.Sensitive);", Case.Sensitive);
    }

    [Fact]
    public async Task does_not_emit_case_sensitive_for_char_in_string_contains()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     var s = "Format";
                                                     Assert.DoesNotContain('\n', s);
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false, true);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldContain("s.ShouldNotContain('\\n');", Case.Sensitive);
        content.ShouldNotContain("Case.Sensitive", Case.Sensitive);
    }

    [Fact]
    public async Task leaves_unsupported_assert_overloads_unchanged()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     Assert.Equal(1.234, 1.235, 2);
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false, true);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldNotContain("using Shouldly;", Case.Sensitive);
        content.ShouldContain("Assert.Equal(1.234, 1.235, 2);", Case.Sensitive);
    }

    [Fact]
    public async Task handles_default_mode_reviews_without_modifying_files()
    {
        using var project = new AdhocProjectBuilder("Tests");
        project.AddSource("ExampleTests.cs", """
                                             using Xunit;

                                             public class ExampleTests
                                             {
                                                 [Fact]
                                                 public void Values_match()
                                                 {
                                                     Assert.True(true);
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();
        var original = await project.ReadSourceAsync("ExampleTests.cs");

        using var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using (workspace)
            {
                await _services.Resolve<ReplaceXunitAssertsCommand>().ExecuteCoreAsync(solution, false);
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        content.ShouldBe(original);
        stdout.ToString().ShouldContain("xunit assert review: 1 call site(s)", Case.Sensitive);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
