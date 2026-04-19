using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Tests.Navigation;

public class FindImplementationsTests
{
    [Fact]
    public async Task Returns_all_interface_implementations()
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
            var namedType = Assert.IsAssignableFrom<INamedTypeSymbol>(iface);

            var impls = await SymbolFinder.FindImplementationsAsync(namedType, sln);
            var implNames = impls.Select(s => s.Name).ToList();

            Assert.Contains("SqlRepository", implNames);
            Assert.Contains("InMemoryRepository", implNames);
            Assert.DoesNotContain("UnrelatedService", implNames);
            Assert.Equal(2, implNames.Count);
        }
    }

    [Fact]
    public async Task Returns_derived_types_of_an_abstract_base()
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
            var namedType = Assert.IsAssignableFrom<INamedTypeSymbol>(baseType);

            var derived = await SymbolFinder.FindDerivedClassesAsync(namedType, sln);
            var derivedNames = derived.Select(s => s.Name).ToList();

            Assert.Contains("CreateHandler", derivedNames);
            Assert.Contains("DeleteHandler", derivedNames);
            Assert.DoesNotContain("StandaloneService", derivedNames);
            Assert.Equal(2, derivedNames.Count);
        }
    }

    [Fact]
    public async Task Returns_nothing_when_no_implementations_exist()
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
            var namedType = Assert.IsAssignableFrom<INamedTypeSymbol>(iface);

            var impls = await SymbolFinder.FindImplementationsAsync(namedType, sln);
            Assert.Empty(impls);
        }
    }
}
