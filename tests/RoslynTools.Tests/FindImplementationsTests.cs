using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class FindImplementationsTests
{
    [Fact]
    public async Task Finds_All_Interface_Implementations()
    {
        using var project = new TestProjectBuilder();
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

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
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
    public async Task Finds_Derived_Classes_Of_Abstract_Base()
    {
        using var project = new TestProjectBuilder();
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

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
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
    public async Task Interface_With_No_Implementations_Returns_Empty()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Lonely.cs", """
            namespace TestApp;

            public interface IUnimplemented
            {
                void DoNothing();
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var iface = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.IUnimplemented", "type");
            var namedType = Assert.IsAssignableFrom<INamedTypeSymbol>(iface);

            var impls = await SymbolFinder.FindImplementationsAsync(namedType, sln);
            Assert.Empty(impls);
        }
    }
}
