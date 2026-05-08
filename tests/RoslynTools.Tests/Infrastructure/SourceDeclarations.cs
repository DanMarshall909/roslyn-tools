using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Services;

namespace RoslynTools.Tests.Infrastructure;

public sealed class SourceDeclarations
{
    [Fact]
    public async Task locate_the_document_that_declares_a_source_symbol()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Order.cs", """
                                      namespace App;

                                      public class Order
                                      {
                                      }
                                      """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var symbol = await GetTypeSymbolAsync(solution, "App.Order");
            var documentId = new DocumentLocatorService().FindDocumentId(solution, symbol);

            Assert.NotNull(documentId);
            Assert.Equal("Order.cs", solution.GetDocument(documentId!)?.Name);
        }
    }

    [Fact]
    public async Task return_the_typed_source_declaration_for_a_symbol()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Order.cs", """
                                      namespace App;

                                      public class Order
                                      {
                                      }
                                      """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var symbol = await GetTypeSymbolAsync(solution, "App.Order");
            var declaration = await new DocumentLocatorService()
                .GetDeclarationAsync<ClassDeclarationSyntax>(solution, symbol);

            Assert.Equal("Order", declaration.Identifier.Text);
        }
    }

    [Fact]
    public async Task explain_when_a_symbol_has_no_source_declaration()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Order.cs", """
                                      namespace App;

                                      public class Order
                                      {
                                      }
                                      """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var compilation = await solution.Projects.Single().GetCompilationAsync();
            var symbol = compilation!.GetSpecialType(SpecialType.System_String);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new DocumentLocatorService().GetDeclarationAsync<ClassDeclarationSyntax>(solution, symbol));

            Assert.Contains("No source declaration found", exception.Message);
            Assert.Contains("string", exception.Message);
        }
    }

    [Fact]
    public async Task explain_when_the_source_declaration_has_a_different_shape()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Order.cs", """
                                      namespace App;

                                      public class Order
                                      {
                                          public void Submit() { }
                                      }
                                      """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var typeSymbol = await GetTypeSymbolAsync(solution, "App.Order");
            var methodSymbol = typeSymbol.GetMembers("Submit").Single();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new DocumentLocatorService().GetDeclarationAsync<ClassDeclarationSyntax>(solution, methodSymbol));

            Assert.Contains("is not a ClassDeclarationSyntax", exception.Message);
        }
    }

    [Fact]
    public async Task return_the_syntax_root_for_a_document()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Order.cs", """
                                      namespace App;

                                      public class Order
                                      {
                                      }
                                      """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var document = solution.Projects.Single().Documents.Single();
            var root = await new DocumentLocatorService().GetSyntaxRootAsync(document);

            Assert.IsAssignableFrom<CompilationUnitSyntax>(root);
        }
    }

    private static async Task<INamedTypeSymbol> GetTypeSymbolAsync(Solution solution, string metadataName)
    {
        var compilation = await solution.Projects.Single().GetCompilationAsync();
        var symbol = compilation!.GetTypeByMetadataName(metadataName);
        return Assert.IsAssignableFrom<INamedTypeSymbol>(symbol);
    }
}
