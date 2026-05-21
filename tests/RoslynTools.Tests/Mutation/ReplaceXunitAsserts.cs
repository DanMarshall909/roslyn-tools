using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;

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

            Assert.Equal(0, exitCode);
        }

        var content = await project.ReadSourceAsync("ExampleTests.cs");
        Assert.Contains("using Shouldly;", content);
        Assert.Contains("actual.ShouldBe(42);", content);
        Assert.Equal(2, CountOccurrences(content, "actual.ShouldBe(42);"));
        Assert.DoesNotContain("Assert.Equal", content);
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
        Assert.Contains("actual.ShouldNotBe(\"wrong\");", content);
        Assert.Contains("other.ShouldBeSameAs(other);", content);
        Assert.Contains("other.ShouldNotBeSameAs(new object());", content);
        Assert.Contains("(actual.Length > 0).ShouldBeTrue();", content);
        Assert.Contains("(actual.Length == 0).ShouldBeFalse();", content);
        Assert.Contains("maybe.ShouldBeNull();", content);
        Assert.Contains("actual.ShouldNotBeNull();", content);
        Assert.Contains("Array.Empty<string>().ShouldBeEmpty();", content);
        Assert.Contains("new[] { \"value\" }.ShouldNotBeEmpty();", content);
        Assert.Contains("actual.ShouldContain(\"value\");", content);
        Assert.Contains("actual.ShouldNotContain(\"missing\");", content);
        Assert.Contains("actual.ShouldStartWith(\"prefix\");", content);
        Assert.Contains("actual.ShouldEndWith(\"value\");", content);
        Assert.Contains("actual.ShouldBeOfType<string>();", content);
        Assert.Contains("actual.ShouldBeAssignableTo<object>();", content);
        Assert.Contains("Should.Throw<InvalidOperationException>(() => throw new InvalidOperationException());",
            content);
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
        Assert.DoesNotContain("using Shouldly;", content);
        Assert.Contains("Assert.Equal(1.234, 1.235, 2);", content);
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
        Assert.Equal(original, content);
        Assert.Contains("xunit assert review: 1 call site(s)", stdout.ToString());
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
