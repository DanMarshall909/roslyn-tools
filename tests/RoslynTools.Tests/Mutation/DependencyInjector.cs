using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class DependencyInjector
{
    private static string Rewrite(string source, string targetClass, string depType, string fieldName, string paramName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetDeclaration = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == targetClass);

        if (targetDeclaration is null)
        {
            var injectorWithoutTarget = new Core.DependencyInjector(targetClass, depType, fieldName, paramName);
            return injectorWithoutTarget.Visit(root)!.ToFullString();
        }

        var compilation = CSharpCompilation.Create(
            "DependencyInjectorTests",
            [tree],
            BuildMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var targetSymbol = semanticModel.GetDeclaredSymbol(targetDeclaration)
                           ?? throw new InvalidOperationException($"Could not resolve target class '{targetClass}'.");
        var depTypeSymbol = semanticModel.GetSpeculativeTypeInfo(
            targetDeclaration.OpenBraceToken.SpanStart,
            SyntaxFactory.ParseTypeName(depType),
            SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
        var injector = new Core.DependencyInjector(
            targetClass,
            depType,
            fieldName,
            paramName,
            semanticModel,
            depTypeSymbol,
            targetSymbol);
        return injector.Visit(root)!.ToFullString();
    }

    private static ConstructorDeclarationSyntax GetFirstConstructor(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        return root.DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .Single();
    }

    [Fact]
    public void rejects_non_matching_class_is_returned_unchanged()
    {
        var source = "class Foo { }";
        var result = Rewrite(source, "Bar", "ILogger", "_logger", "logger");
        result.ShouldBe(source);
    }

    [Fact]
    public void adds_private_readonly_field()
    {
        var source = "class MyService { }";
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        result.ShouldContain("private readonly ILogger _logger;", Case.Sensitive);
    }

    [Fact]
    public void creates_constructor_when_none_exists()
    {
        var source = "class MyService { }";
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        result.ShouldContain("public MyService(ILogger logger)", Case.Sensitive);
        result.ShouldContain("_logger = logger;", Case.Sensitive);
    }

    [Fact]
    public void adds_parameter_to_existing_constructor()
    {
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public MyService(IFoo foo) { _foo = foo; }
                     }
                     """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        result.ShouldContain("ILogger logger", Case.Sensitive);
        result.ShouldContain("_logger = logger;", Case.Sensitive);
    }

    [Fact]
    public void does_not_duplicate_field_if_already_injected()
    {
        var source = """
                     class MyService
                     {
                         private readonly ILogger _logger;
                         public MyService(ILogger logger) { _logger = logger; }
                     }
                     """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        CountOccurrences(result, "private readonly ILogger _logger").ShouldBe(1);
    }

    [Fact]
    public void skips_constructor_that_already_has_the_dep_type()
    {
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public MyService(IFoo foo, ILogger logger) { }
                     }
                     """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        result.ShouldContain("private readonly ILogger _logger;", Case.Sensitive);
        CountOccurrences(result, "ILogger logger").ShouldBe(1);
    }

    [Fact]
    public void does_not_duplicate_field_when_class_has_multiple_other_fields()
    {
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         private readonly ILogger _logger;
                         public MyService(IFoo foo, ILogger logger) { }
                     }
                     """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        CountOccurrences(result, "private readonly ILogger _logger").ShouldBe(1);
    }

    [Fact]
    public void does_not_duplicate_field_when_it_is_declared_with_another_variable()
    {
        var source = """
                     class MyService
                     {
                         private readonly ILogger _other, _logger;
                         public MyService(ILogger logger) { _logger = logger; }
                     }
                     """;

        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        var fields = CSharpSyntaxTree.ParseText(result).GetRoot().DescendantNodes()
            .OfType<FieldDeclarationSyntax>();
        var loggerDeclarators = fields.SelectMany(f => f.Declaration.Variables)
            .Count(v => v.Identifier.Text == "_logger");

        loggerDeclarators.ShouldBe(1);
        result.ShouldNotContain("private readonly ILogger _logger;", Case.Sensitive);
    }

    [Fact]
    public void inserts_new_field_after_existing_fields_not_before_methods()
    {
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public void FooMethod() { }
                     }
                     """;
        var result = Rewrite(source, "MyService", "IBar", "_bar", "bar");
        var fooIdx = result.IndexOf("IFoo _foo", StringComparison.Ordinal);
        var barIdx = result.IndexOf("IBar _bar", StringComparison.Ordinal);
        Assert.True(fooIdx >= 0, "original field _foo not found");
        Assert.True(barIdx >= 0, "injected field _bar not found");
        Assert.True(fooIdx < barIdx, "_bar should be inserted AFTER _foo, not before it");
    }

    [Fact]
    public void inserts_new_field_before_non_contiguous_fields()
    {
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public void FooMethod() { }
                         private readonly IBar _bar;
                     }
                     """;

        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        var fooIdx = result.IndexOf("IFoo _foo", StringComparison.Ordinal);
        var loggerIdx = result.IndexOf("ILogger _logger", StringComparison.Ordinal);
        var methodIdx = result.IndexOf("public void FooMethod", StringComparison.Ordinal);

        Assert.True(fooIdx < loggerIdx, "new field should follow the initial field run");
        Assert.True(loggerIdx < methodIdx, "new field should not move past methods to later field declarations");
    }

    [Fact]
    public void inserts_synthesized_constructor_after_new_field_before_methods()
    {
        var source = """
                     class MyService
                     {
                         public void FooMethod() { }
                     }
                     """;

        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        var fieldIdx = result.IndexOf("ILogger _logger", StringComparison.Ordinal);
        var ctorIdx = result.IndexOf("public MyService(ILogger logger)", StringComparison.Ordinal);
        var methodIdx = result.IndexOf("public void FooMethod", StringComparison.Ordinal);

        Assert.True(fieldIdx >= 0, "injected field not found");
        Assert.True(ctorIdx >= 0, "synthesized constructor not found");
        Assert.True(methodIdx >= 0, "original method not found");
        Assert.True(fieldIdx < ctorIdx, "constructor should be inserted after the injected field");
        Assert.True(ctorIdx < methodIdx, "constructor should be inserted before methods");
    }

    [Fact]
    public void inserts_synthesized_constructor_after_existing_field_before_methods()
    {
        var source = """
                     class MyService
                     {
                         private readonly ILogger _logger;
                         public void FooMethod() { }
                     }
                     """;

        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        var fieldIdx = result.IndexOf("ILogger _logger", StringComparison.Ordinal);
        var ctorIdx = result.IndexOf("public MyService(ILogger logger)", StringComparison.Ordinal);
        var methodIdx = result.IndexOf("public void FooMethod", StringComparison.Ordinal);

        Assert.True(fieldIdx < ctorIdx, "constructor should be inserted after existing fields");
        Assert.True(ctorIdx < methodIdx, "constructor should be inserted before methods");
    }

    [Fact]
    public void skips_replacement_when_all_constructors_already_have_dep_type()
    {
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public MyService(IFoo foo, ILogger logger) { }
                         public MyService(IFoo foo, ILogger logger, string extra) { }
                     }
                     """;
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        result.ShouldContain("private readonly ILogger _logger;", Case.Sensitive);
        CountOccurrences(result, "ILogger logger").ShouldBe(2);
    }

    [Fact]
    public void reuses_a_matching_constructor_parameter_without_duplicating_it()
    {
        var source = """
                     using Alias = MyApp.Logging.ILogger;

                     namespace MyApp.Logging
                     {
                         public interface ILogger { }
                     }

                     class MyService
                     {
                         public MyService(Alias logger)
                         {
                         }
                     }
                     """;

        var result = Rewrite(source, "MyService", "MyApp.Logging.ILogger", "_logger", "logger");
        var constructor = GetFirstConstructor(result);

        Assert.Single(constructor.ParameterList.Parameters);
        result.ShouldContain("_logger = logger;", Case.Sensitive);
    }

    [Fact]
    public void reuses_a_matching_global_qualified_parameter_without_duplicating_it()
    {
        var source = """
                     namespace MyApp.Logging
                     {
                         public interface ILogger { }
                     }

                     class MyService
                     {
                         public MyService(global::MyApp.Logging.ILogger logger)
                         {
                         }
                     }
                     """;

        var result = Rewrite(source, "MyService", "MyApp.Logging.ILogger", "_logger", "logger");
        var constructor = GetFirstConstructor(result);

        Assert.Single(constructor.ParameterList.Parameters);
        result.ShouldContain("_logger = logger;", Case.Sensitive);
    }

    [Fact]
    public void does_not_leave_expression_bodied_constructors_with_an_unassigned_dependency_parameter()
    {
        var source = """
                     interface ILogger { }

                     class MyService
                     {
                         public MyService() => Initialize();

                         private void Initialize()
                         {
                         }
                     }
                     """;

        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        var constructor = GetFirstConstructor(result);

        Assert.True(
            constructor.ExpressionBody is null || constructor.ParameterList.Parameters.Count == 0,
            "expression-bodied constructors must not gain an injected parameter without a corresponding assignment rewrite");
    }

    [Fact]
    public void expression_bodied_constructor_reports_unsupported_with_actionable_error()
    {
        var source = """
                     interface ILogger { }

                     class MyService
                     {
                         public MyService() => Initialize();

                         private void Initialize()
                         {
                         }
                     }
                     """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var injector = new Core.DependencyInjector("MyService", "ILogger", "_logger", "logger");

        injector.Visit(tree.GetRoot());

        injector.Status.ShouldBe(DependencyInjectionStatus.Unsupported);
        injector.Applied.ShouldBeFalse();
        injector.ValidationError!.ShouldContain("MyService", Case.Sensitive);
        injector.ValidationError!.ShouldContain("_logger", Case.Sensitive);
    }

    [Fact]
    public void expression_bodied_constructor_with_parameter_but_no_assignment_reports_unsupported()
    {
        var source = """
                     interface ILogger { }

                     class MyService
                     {
                         public MyService(ILogger logger) => Initialize(logger);

                         private void Initialize(ILogger logger)
                         {
                         }
                     }
                     """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var injector = new Core.DependencyInjector("MyService", "ILogger", "_logger", "logger");

        injector.Visit(tree.GetRoot());

        injector.Status.ShouldBe(DependencyInjectionStatus.Unsupported);
        injector.ValidationError!.ShouldContain("_logger", Case.Sensitive);
    }

    [Fact]
    public void expression_bodied_constructor_with_assignment_is_already_injected()
    {
        var source = """
                     interface ILogger { }

                     class MyService
                     {
                         private readonly ILogger _logger;
                         public MyService(ILogger logger) => _logger = logger;
                     }
                     """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var injector = new Core.DependencyInjector("MyService", "ILogger", "_logger", "logger");

        injector.Visit(tree.GetRoot());

        injector.Status.ShouldBe(DependencyInjectionStatus.AlreadyInjected);
        injector.ValidationError.ShouldBeNull();
    }

    [Fact]
    public void adds_assignment_when_constructor_parameter_assigns_a_different_field()
    {
        var source = """
                     class MyService
                     {
                         private readonly ILogger _logger;
                         private readonly ILogger _other;
                         public MyService(ILogger logger) { _other = logger; }
                     }
                     """;

        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");

        result.ShouldContain("_logger = logger;", Case.Sensitive);
        result.ShouldContain("_other = logger;", Case.Sensitive);
    }

    [Fact]
    public void target_symbol_without_semantic_model_does_not_match_by_name()
    {
        var source = "class MyService { }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "DependencyInjectorTargetTests",
            [tree],
            BuildMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var targetType = compilation.GetSpecialType(SpecialType.System_Object);
        var injector = new Core.DependencyInjector(
            "MyService",
            "ILogger",
            "_logger",
            "logger",
            targetTypeSymbol: targetType);

        var result = injector.Visit(tree.GetRoot())!.ToFullString();

        result.ShouldBe(source);
        injector.Status.ShouldBe(DependencyInjectionStatus.ClassNotFound);
    }

    [Fact]
    public void matches_untyped_parameter_by_normalized_text_when_semantics_are_unavailable()
    {
        var source = """
                     class MyService
                     {
                         private readonly ILogger _logger;
                         public MyService(global::ILogger logger) { _logger = logger; }
                     }
                     """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var injector = new Core.DependencyInjector("MyService", "ILogger", "_logger", "logger");

        var result = injector.Visit(tree.GetRoot())!.ToFullString();
        var constructor = GetFirstConstructor(result);

        Assert.Single(constructor.ParameterList.Parameters);
        injector.Status.ShouldBe(DependencyInjectionStatus.AlreadyInjected);
    }

    [Fact]
    public void normalizes_internal_whitespace_in_generic_type_when_matching_parameter()
    {
        // Kills NormalizeTypeName's " "→"Stryker was here!" mutation: an existing parameter
        // typed `IDictionary<int, string>` (with a space after the comma) must be recognised
        // as already injecting `IDictionary<int,string>` (without the space).
        var source = """
                     class MyService
                     {
                         private readonly IDictionary<int, string> _map;
                         public MyService(IDictionary<int, string> map) { _map = map; }
                     }
                     """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var injector = new Core.DependencyInjector("MyService", "IDictionary<int,string>", "_map", "map");

        injector.Visit(tree.GetRoot());

        injector.Status.ShouldBe(DependencyInjectionStatus.AlreadyInjected);
    }

    [Fact]
    public void synthesized_constructor_has_valid_block_body()
    {
        var source = "class MyService { }";
        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        var tree = CSharpSyntaxTree.ParseText(result);
        var constructor = tree.GetRoot().DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .Single();

        tree.GetDiagnostics().ShouldBeEmpty();
        constructor.Body.ShouldNotBeNull();
        Assert.Single(constructor.Body!.Statements);
        constructor.Body.Statements[0].ToString().ShouldBe("_logger = logger;");
    }

    // ─── parameter list formatting ────────────────────────────────────────────

    [Fact]
    public void new_param_on_own_line_when_existing_params_are_one_per_line()
    {
        // When existing params each occupy their own line, the new param must also
        // appear on a fresh line with matching indentation — not glued to the last param.
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public MyService(
                             IFoo foo)
                         {
                             _foo = foo;
                         }
                     }
                     """;
        var result = Rewrite(source, "MyService", "IBar", "_bar", "bar");
        // locate the ctor param list
        var ctorStart = result.IndexOf("public MyService(", StringComparison.Ordinal);
        var parenOpen = result.IndexOf('(', ctorStart);
        var parenClose = result.IndexOf(')', parenOpen);
        var paramText = result[parenOpen..parenClose];
        paramText.ShouldContain("IFoo foo", Case.Sensitive);
        paramText.ShouldContain("IBar bar", Case.Sensitive);
        // the two params must be on separate lines
        var fooIdx = paramText.IndexOf("IFoo foo", StringComparison.Ordinal);
        var barIdx = paramText.IndexOf("IBar bar", StringComparison.Ordinal);
        var between = paramText[fooIdx..barIdx];
        between.ShouldContain('\n');
    }

    [Fact]
    public void new_param_has_leading_space_when_existing_params_are_single_line()
    {
        // When all existing params fit on one line, the new param must be separated
        // by ", " — not ",IBar bar" (missing space) and not ",\n    IBar bar" (unexpected newline).
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public MyService(IFoo foo) { _foo = foo; }
                     }
                     """;
        var result = Rewrite(source, "MyService", "IBar", "_bar", "bar");
        result.ShouldContain("IFoo foo, IBar bar", Case.Sensitive);
    }

    [Fact]
    public void new_param_has_no_stray_leading_chars_when_ctor_had_no_params()
    {
        // When synthesising a fresh constructor for a class with no ctors, the
        // parameter list must be "(IBar bar)" — not "( IBar bar)" or "(,IBar bar)".
        var source = "class MyService { }";
        var result = Rewrite(source, "MyService", "IBar", "_bar", "bar");
        result.ShouldContain("(IBar bar)", Case.Sensitive);
    }

    [Fact]
    public void new_param_has_no_stray_leading_chars_when_existing_ctor_had_no_params()
    {
        var source = """
                     class MyService
                     {
                         public MyService() { }
                     }
                     """;

        var result = Rewrite(source, "MyService", "ILogger", "_logger", "logger");
        var constructor = GetFirstConstructor(result);

        constructor.ParameterList.ToString().ShouldBe("(ILogger logger)");
        constructor.ParameterList.ToFullString().ShouldNotContain("( ILogger logger)", Case.Sensitive);
    }

    // ─── delegating constructor (this(...)) scenarios ─────────────────────────

    [Fact]
    public void delegating_ctor_with_dep_already_in_full_ctor_reports_already_injected()
    {
        var source = """
                     class MyService
                     {
                         private readonly ILogger _logger;
                         public MyService() : this(new DefaultLogger()) { }
                         public MyService(ILogger logger) { _logger = logger; }
                     }
                     """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var injector = new Core.DependencyInjector("MyService", "ILogger", "_logger", "logger");
        injector.Visit(tree.GetRoot());
        injector.Status.ShouldBe(DependencyInjectionStatus.AlreadyInjected);
    }

    [Fact]
    public void delegating_ctor_with_new_dep_only_modifies_full_ctor()
    {
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public MyService() : this(new DefaultFoo()) { }
                         public MyService(IFoo foo) { _foo = foo; }
                     }
                     """;
        var result = Rewrite(source, "MyService", "IBar", "_bar", "bar");
        // fallback ctor stays parameterless
        result.ShouldNotContain("public MyService(IBar", Case.Sensitive);
        result.ShouldNotContain("MyService() : this(new DefaultFoo()) {\r\n        _bar", Case.Sensitive);
        // full ctor gets the new param
        result.ShouldContain("IBar bar", Case.Sensitive);
        result.ShouldContain("_bar = bar;", Case.Sensitive);
    }

    [Fact]
    public void multiple_chained_delegating_ctors_only_modifies_terminal_ctor()
    {
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public MyService() : this(42) { }
                         public MyService(int value) : this(value, new DefaultFoo()) { }
                         public MyService(int value, IFoo foo) { _foo = foo; }
                     }
                     """;
        var result = Rewrite(source, "MyService", "IBar", "_bar", "bar");
        // only one occurrence of new param — in the terminal ctor
        CountOccurrences(result, "IBar bar").ShouldBe(1);
        // neither delegating ctor was touched
        result.ShouldContain("public MyService() : this(42) { }", Case.Sensitive);
        result.ShouldContain("public MyService(int value) : this(value, new DefaultFoo()) { }", Case.Sensitive);
    }

    [Fact]
    public void single_non_delegating_ctor_still_gets_parameter_added()
    {
        // regression guard: the fix must not break the normal (non-delegating) ctor path
        var source = """
                     class MyService
                     {
                         private readonly IFoo _foo;
                         public MyService(IFoo foo) { _foo = foo; }
                     }
                     """;
        var result = Rewrite(source, "MyService", "IBar", "_bar", "bar");
        result.ShouldContain("IBar bar", Case.Sensitive);
        result.ShouldContain("_bar = bar;", Case.Sensitive);
    }

    [Fact]
    public void no_ctor_class_still_gets_synthesized_constructor()
    {
        // regression guard: the fix must not break the zero-ctor synthesis path
        var source = "class MyService { }";
        var result = Rewrite(source, "MyService", "IBar", "_bar", "bar");
        result.ShouldContain("public MyService(IBar bar)", Case.Sensitive);
        result.ShouldContain("_bar = bar;", Case.Sensitive);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }

        return count;
    }

    private static IEnumerable<MetadataReference> BuildMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                                        ?? throw new InvalidOperationException(
                                            "TRUSTED_PLATFORM_ASSEMBLIES is not set.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path));
    }
}
