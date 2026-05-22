using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class Move
{
    [Fact]
    public async Task updates_the_namespace_declaration()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Model.cs", """
                                      namespace TestApp.OldNamespace;

                                      public class Widget
                                      {
                                          public int Id { get; set; }
                                      }
                                      """);
        project.AddSource("Consumer.cs", """
                                         using TestApp.OldNamespace;

                                         namespace TestApp;

                                         public class WidgetService
                                         {
                                             public Widget Get() => new Widget();
                                         }
                                         """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Rewrite namespace declaration
            var sym = await SymbolLocator.FindTypeByFqnAsync(sln, "TestApp.OldNamespace.Widget");
            foreach (var syntaxRef in sym.DeclaringSyntaxReferences)
            {
                var doc = sln.GetDocument(syntaxRef.SyntaxTree)!;
                var root = (await doc.GetSyntaxRootAsync())!;
                var node = await syntaxRef.GetSyntaxAsync();
                var nsDecl = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().First();

                var newName = SyntaxFactory.ParseName("TestApp.NewNamespace").WithTriviaFrom(nsDecl.Name);
                var newRoot = root.ReplaceNode(nsDecl, nsDecl.WithName(newName));
                sln = sln.WithDocumentSyntaxRoot(doc.Id, newRoot);
            }

            await SolutionWriter.ApplyChangesAsync(workspace.CurrentSolution, sln, false);
        }

        var modelContent = await project.ReadSourceAsync("Model.cs");
        modelContent.ShouldContain("namespace TestApp.NewNamespace", Case.Sensitive);
        modelContent.ShouldNotContain("OldNamespace", Case.Sensitive);
    }

    [Fact]
    public async Task rewrites_all_types_in_the_batch()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("ModelsA.cs", """
                                        namespace TestApp.Legacy;

                                        public class Customer
                                        {
                                            public int Id { get; set; }
                                        }
                                        """);
        project.AddSource("ModelsB.cs", """
                                        namespace TestApp.Legacy;

                                        public class Order
                                        {
                                            public int Id { get; set; }
                                            public int CustomerId { get; set; }
                                        }
                                        """);
        project.AddSource("Service.cs", """
                                        using TestApp.Legacy;

                                        namespace TestApp.Services;

                                        public class OrderService
                                        {
                                            public Customer GetCustomer(int id) => new Customer();
                                            public Order CreateOrder(Customer c) => new Order { CustomerId = c.Id };
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Batch rewrite: change all "TestApp.Legacy" -> "TestApp.Domain"
            foreach (var proj in sln.Projects)
                foreach (var doc in proj.Documents)
                {
                    var root = await doc.GetSyntaxRootAsync();
                    if (root == null) continue;

                    var rewriter = new NamespaceRewriter("TestApp.Legacy", "TestApp.Domain");
                    var newRoot = rewriter.Visit(root);

                    if (rewriter.Changed)
                        sln = sln.WithDocumentSyntaxRoot(doc.Id, newRoot);
                }

            await SolutionWriter.ApplyChangesAsync(workspace.CurrentSolution, sln, false);
        }

        var modelsA = await project.ReadSourceAsync("ModelsA.cs");
        modelsA.ShouldContain("namespace TestApp.Domain", Case.Sensitive);
        modelsA.ShouldNotContain("Legacy", Case.Sensitive);

        var modelsB = await project.ReadSourceAsync("ModelsB.cs");
        modelsB.ShouldContain("namespace TestApp.Domain", Case.Sensitive);
        modelsB.ShouldNotContain("Legacy", Case.Sensitive);

        var service = await project.ReadSourceAsync("Service.cs");
        service.ShouldContain("TestApp.Domain", Case.Sensitive);
    }

    [Fact]
    public async Task does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Target.cs", """
                                       namespace TestApp.Old;

                                       public class Thing
                                       {
                                           public int Value { get; set; }
                                       }
                                       """);

        var (workspace, sln) = project.Build();
        var original = await project.ReadSourceAsync("Target.cs");
        using (workspace)
        {
            var sym = await SymbolLocator.FindTypeByFqnAsync(sln, "TestApp.Old.Thing");
            foreach (var syntaxRef in sym.DeclaringSyntaxReferences)
            {
                var doc = sln.GetDocument(syntaxRef.SyntaxTree)!;
                var root = (await doc.GetSyntaxRootAsync())!;
                var node = await syntaxRef.GetSyntaxAsync();
                var nsDecl = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().First();

                var newName = SyntaxFactory.ParseName("TestApp.New").WithTriviaFrom(nsDecl.Name);
                var newRoot = root.ReplaceNode(nsDecl, nsDecl.WithName(newName));
                sln = sln.WithDocumentSyntaxRoot(doc.Id, newRoot);
            }

            await SolutionWriter.ApplyChangesAsync(workspace.CurrentSolution, sln, true);
        }

        var afterContent = await project.ReadSourceAsync("Target.cs");
        afterContent.ShouldBe(original);
    }

    /// <summary>
    ///     Minimal syntax rewriter for test purposes — mirrors the pattern in MoveCommand.
    /// </summary>
    private sealed class NamespaceRewriter : CSharpSyntaxRewriter
    {
        private readonly string _newNs;
        private readonly string _oldNs;

        public NamespaceRewriter(string oldNs, string newNs)
        {
            _oldNs = oldNs;
            _newNs = newNs;
        }

        public bool Changed { get; private set; }

        public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            var visited = (FileScopedNamespaceDeclarationSyntax)base.VisitFileScopedNamespaceDeclaration(node)!;
            if (visited.Name.ToString() == _oldNs)
            {
                Changed = true;
                return visited.WithName(SyntaxFactory.ParseName(_newNs).WithTriviaFrom(visited.Name));
            }

            return visited;
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var visited = (QualifiedNameSyntax)base.VisitQualifiedName(node)!;
            if (visited.ToString() == _oldNs)
            {
                Changed = true;
                return SyntaxFactory.ParseName(_newNs).WithTriviaFrom(visited);
            }

            return visited;
        }
    }
}
