using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;

namespace RoslynTools.Tests.Mutation;

public class SafeDelete : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public SafeDelete(RtServicesFixture services) => _services = services;

    private const string SafeAppSource = """
                                         public class Orphan { }

                                         public class Used { public void Go() { } }

                                         public class Caller { void Run() { var x = new Used(); } }

                                         public class Svc
                                         {
                                             public void Dead() { }
                                             public void Live() { }
                                             void CallLive() { Live(); }
                                         }
                                         """;

    private static (AdhocProjectBuilder Project, Workspace Workspace, Solution Solution) BuildFixture()
    {
        var project = new AdhocProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", SafeAppSource);
        var (workspace, sln) = project.Build();
        return (project, workspace, sln);
    }

    [Fact]
    public async Task deletes_the_type()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<SafeDeleteCommand>().ExecuteAsync("Orphan", null, "ignored", false);

            var content = await project.ReadSourceAsync("SafeApp.cs");
            Assert.DoesNotContain("class Orphan", content);
        }
    }

    [Fact]
    public async Task throws_an_error_for_a_referenced_type()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _services.Resolve<SafeDeleteCommand>().ExecuteAsync("Used", null, "ignored", false));

            Assert.Contains("Used", ex.Message);
        }
    }

    [Fact]
    public async Task deletes_the_method()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<SafeDeleteCommand>().ExecuteAsync("Svc.Dead", "method", "ignored", false);

            var content = await project.ReadSourceAsync("SafeApp.cs");
            Assert.DoesNotContain("void Dead()", content);
            Assert.Contains("void Live()", content);
        }
    }

    [Fact]
    public async Task throws_an_error_for_a_referenced_method()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _services.Resolve<SafeDeleteCommand>().ExecuteAsync("Svc.Live", "method", "ignored", false));

            Assert.Contains("Live", ex.Message);
        }
    }

    [Fact]
    public async Task does_not_modify_files()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var originalContent = await project.ReadSourceAsync("SafeApp.cs");

            await _services.Resolve<SafeDeleteCommand>().ExecuteAsync("Orphan", null, "ignored", true);

            var afterContent = await project.ReadSourceAsync("SafeApp.cs");
            Assert.Equal(originalContent, afterContent);
        }
    }

    [Fact]
    public async Task treats_nameof_references_as_non_references_and_deletes()
    {
        var project = new AdhocProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", """
                                        public class Logger
                                        {
                                            public void Log(string msg) { }
                                        }

                                        public class Client
                                        {
                                            void Setup()
                                            {
                                                // nameof reference only — not a real usage
                                                var name = nameof(Logger);
                                            }
                                        }
                                        """);
        var (workspace, sln) = project.Build();

        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            // Logger is only used inside nameof() — should be deletable
            await _services.Resolve<SafeDeleteCommand>().ExecuteAsync("Logger", null, "ignored", false);

            var content = await project.ReadSourceAsync("SafeApp.cs");
            Assert.DoesNotContain("class Logger", content);
        }
    }

    [Fact]
    public async Task delete_a_simple_member_name_after_type_lookup_falls_back()
    {
        var project = new AdhocProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", """
                                        public class Svc
                                        {
                                            public void Dead() { }
                                            public void Live() { }
                                        }
                                        """);
        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        using (project)
        {
            await new SafeDeleteService(NullLogger<SafeDeleteService>.Instance, writer)
                .RunAsync(solution, "Dead", null, false);

            var content = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "SafeApp.cs");
            Assert.DoesNotContain("void Dead()", content);
            Assert.Contains("void Live()", content);
        }
    }

    [Fact]
    public void support_all_top_level_member_declarations_that_safe_delete_can_remove()
    {
        var root = CSharpSyntaxTree.ParseText("""
                                              public class C { }
                                              public interface I { }
                                              public struct S { }
                                              public record R;
                                              public enum E { A }
                                              public delegate void D();
                                              public class Members
                                              {
                                                  public int Field;
                                                  public event EventHandler? Changed;
                                                  public int Property { get; set; }
                                                  public void Method() { }
                                              }
                                              """).GetCompilationUnitRoot();
        var service =
            new SafeDeleteService(NullLogger<SafeDeleteService>.Instance, new CapturingSolutionChangeWriter());

        Assert.All(root.DescendantNodes().OfType<ClassDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.All(root.DescendantNodes().OfType<InterfaceDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.All(root.DescendantNodes().OfType<StructDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.All(root.DescendantNodes().OfType<RecordDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.All(root.DescendantNodes().OfType<EnumDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.All(root.DescendantNodes().OfType<DelegateDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.All(root.DescendantNodes().OfType<MethodDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.All(root.DescendantNodes().OfType<PropertyDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.All(root.DescendantNodes().OfType<FieldDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.All(root.DescendantNodes().OfType<EventDeclarationSyntax>(), node =>
            Assert.Same(node, service.FindDeclarationNode(node)));
        Assert.Null(service.FindDeclarationNode(root));
    }
}
