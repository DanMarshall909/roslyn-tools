using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public class FindImplementations
{
    [Fact]
    public async Task returns_all_interface_implementations()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Contracts.cs", """
                                          namespace TestApp;

                                          public interface IRepository
                                          {
                                              void Save(string item);
                                              string? GetById(int id);
                                          }
                                          """);
        project.AddSource("SqlRepo.cs", """
                                        namespace TestApp;

                                        public class SqlRepository : IRepository
                                        {
                                            public void Save(string item) { }
                                            public string? GetById(int id) => null;
                                        }
                                        """);
        project.AddSource("InMemoryRepo.cs", """
                                             namespace TestApp;

                                             public class InMemoryRepository : IRepository
                                             {
                                                 public void Save(string item) { }
                                                 public string? GetById(int id) => null;
                                             }
                                             """);
        project.AddSource("Unrelated.cs", """
                                          namespace TestApp;

                                          public class UnrelatedService { }
                                          """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var iface = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.IRepository", "type");
            var namedType = iface.ShouldBeAssignableTo<INamedTypeSymbol>()!;

            var impls = await SymbolFinder.FindImplementationsAsync(namedType, sln);
            var implNames = impls.Select(s => s.Name).ToList();

            implNames.ShouldContain("SqlRepository");
            implNames.ShouldContain("InMemoryRepository");
            implNames.ShouldNotContain("UnrelatedService");
            implNames.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task returns_derived_types_of_an_abstract_base()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Types.cs", """
                                      namespace TestApp;

                                      public abstract class BaseHandler
                                      {
                                          public abstract void Handle();
                                      }

                                      public class CreateHandler : BaseHandler
                                      {
                                          public override void Handle() { }
                                      }

                                      public class DeleteHandler : BaseHandler
                                      {
                                          public override void Handle() { }
                                      }

                                      public class StandaloneService { }
                                      """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var baseType = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.BaseHandler", "type");
            var namedType = baseType.ShouldBeAssignableTo<INamedTypeSymbol>()!;

            var derived = await SymbolFinder.FindDerivedClassesAsync(namedType, sln);
            var derivedNames = derived.Select(s => s.Name).ToList();

            derivedNames.ShouldContain("CreateHandler");
            derivedNames.ShouldContain("DeleteHandler");
            derivedNames.ShouldNotContain("StandaloneService");
            derivedNames.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task returns_nothing_when_no_implementations_exist()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Lonely.cs", """
                                       namespace TestApp;

                                       public interface IUnimplemented
                                       {
                                           void DoNothing();
                                       }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var iface = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.IUnimplemented", "type");
            var namedType = iface.ShouldBeAssignableTo<INamedTypeSymbol>()!;

            var impls = await SymbolFinder.FindImplementationsAsync(namedType, sln);
            impls.ShouldBeEmpty();
        }
    }
}
