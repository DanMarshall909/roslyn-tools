using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public sealed class DependencyInjectorTests
{
    private static string Rewrite(string source, string targetClass, string depType, string fieldName, string paramName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var injector = new DependencyInjector(targetClass, depType, fieldName, paramName);
        return injector.Visit(root)!.ToFullString();
    }

    [Fact]
    public void Non_matching_class_is_returned_unchanged()
    {
        var source = "class Foo { }";
        var result = Rewrite(source, "Bar", "ILogger", "_logger", "logger");
        Assert.Equal(source, result);
    }

    [Fact]
    public void Adds_private_readonly_field()
    {
        var source = "class MyService { }";
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        Assert.Contains("private readonly ILogger _logger;", result);
    }

    [Fact]
    public void Creates_constructor_when_none_exists()
    {
        var source = "class MyService { }";
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        Assert.Contains("public MyService(ILogger logger)", result);
        Assert.Contains("_logger = logger;", result);
    }

    [Fact]
    public void Adds_parameter_to_existing_constructor()
    {
        var source = """
            class MyService
            {
                private readonly IFoo _foo;
                public MyService(IFoo foo) { _foo = foo; }
            }
            """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        Assert.Contains("ILogger logger", result);
        Assert.Contains("_logger = logger;", result);
    }

    [Fact]
    public void Does_not_duplicate_field_if_already_injected()
    {
        var source = """
            class MyService
            {
                private readonly ILogger _logger;
                public MyService(ILogger logger) { _logger = logger; }
            }
            """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        // Field should appear exactly once
        Assert.Equal(1, CountOccurrences(result, "private readonly ILogger _logger"));
    }

    [Fact]
    public void Skips_constructor_that_already_has_the_dep_type()
    {
        var source = """
            class MyService
            {
                private readonly IFoo _foo;
                public MyService(IFoo foo, ILogger logger) { }
            }
            """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        // No new ctor parameter added since ILogger already present
        // But field is added
        Assert.Contains("private readonly ILogger _logger;", result);
        // Constructor should remain unchanged (ILogger not added again)
        var ctorCount = CountOccurrences(result, "ILogger logger");
        Assert.Equal(1, ctorCount);
    }

    [Fact]
    public void Does_not_duplicate_field_when_class_has_multiple_other_fields()
    {
        // Kills Any()→All() mutant: if All() were used, it would be false (not all fields match _logger)
        // and would inject a duplicate _logger field.
        var source = """
            class MyService
            {
                private readonly IFoo _foo;
                private readonly ILogger _logger;
                public MyService(IFoo foo, ILogger logger) { }
            }
            """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        Assert.Equal(1, CountOccurrences(result, "private readonly ILogger _logger"));
    }

    [Fact]
    public void Inserts_new_field_after_existing_fields_not_before_methods()
    {
        // Kills L46 negate and L49 statement removal mutants: if insertIndex stays 0
        // or the condition is negated, the new field would be inserted at position 0 (before existing fields).
        var source = """
            class MyService
            {
                private readonly IFoo _foo;
                public void FooMethod() { }
            }
            """;
        var result = Rewrite(source, "MyService", "IBar", "_bar", "bar");
        // _foo field should appear before _bar field (both before the method)
        var fooIdx = result.IndexOf("IFoo _foo", StringComparison.Ordinal);
        var barIdx = result.IndexOf("IBar _bar", StringComparison.Ordinal);
        Assert.True(fooIdx >= 0, "original field _foo not found");
        Assert.True(barIdx >= 0, "injected field _bar not found");
        Assert.True(fooIdx < barIdx, "_bar should be inserted AFTER _foo, not before it");
    }

    [Fact]
    public void Skips_replacement_when_all_constructors_already_have_dep_type()
    {
        // Kills L84 mutation (Count > 0 → Count >= 0): verifies replacements is empty
        // when all ctors already have the dep, so no replacement occurs.
        var source = """
            class MyService
            {
                private readonly IFoo _foo;
                public MyService(IFoo foo, ILogger logger) { }
                public MyService(IFoo foo, ILogger logger, string extra) { }
            }
            """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        // Both ctors already have ILogger — no extra param added, but field IS added
        Assert.Contains("private readonly ILogger _logger;", result);
        // Count of "ILogger logger" should be exactly 2 (one per existing ctor, none added)
        Assert.Equal(2, CountOccurrences(result, "ILogger logger"));
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
