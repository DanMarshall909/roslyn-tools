using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public sealed class DependencyInjectorTests
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
            var injectorWithoutTarget = new DependencyInjector(targetClass, depType, fieldName, paramName);
            return injectorWithoutTarget.Visit(root)!.ToFullString();
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "DependencyInjectorTests",
            syntaxTrees: [tree],
            references: BuildMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var targetSymbol = semanticModel.GetDeclaredSymbol(targetDeclaration) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Could not resolve target class '{targetClass}'.");
        var depTypeSymbol = semanticModel.GetSpeculativeTypeInfo(
            targetDeclaration.OpenBraceToken.SpanStart,
            SyntaxFactory.ParseTypeName(depType),
            SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
        var injector = new DependencyInjector(
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
        Assert.Contains("private readonly ILogger _logger;", result);
        Assert.Equal(1, CountOccurrences(result, "ILogger logger"));
    }

    [Fact]
    public void Does_not_duplicate_field_when_class_has_multiple_other_fields()
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
        Assert.Equal(1, CountOccurrences(result, "private readonly ILogger _logger"));
    }

    [Fact]
    public void Inserts_new_field_after_existing_fields_not_before_methods()
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
    public void Skips_replacement_when_all_constructors_already_have_dep_type()
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
        Assert.Contains("private readonly ILogger _logger;", result);
        Assert.Equal(2, CountOccurrences(result, "ILogger logger"));
    }

    [Fact]
    public void Reuses_a_matching_constructor_parameter_without_duplicating_it()
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
        Assert.Contains("_logger = logger;", result);
    }

    [Fact]
    public void Does_not_leave_expression_bodied_constructors_with_an_unassigned_dependency_parameter()
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

    private static IEnumerable<MetadataReference> BuildMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not set.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path));
    }
}
