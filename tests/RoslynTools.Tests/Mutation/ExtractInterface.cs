using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ExtractInterface : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ExtractInterface(RtServicesFixture services) => _services = services;

    [Fact]
    public async Task creates_interface_file()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
                                             namespace MyApp.Services;

                                             public class AgentService
                                             {
                                                 public string GetAgent(int id) => id.ToString();
                                                 public void DeleteAgent(int id) { }
                                                 private void Helper() { }
                                             }
                                             """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync(
                "AgentService",
                "ignored",
                null,
                null,
                false);
        }

        Assert.True(project.SourceExists("IAgentService.cs"),
            "IAgentService.cs should have been created alongside AgentService.cs");
    }

    [Fact]
    public async Task rejects_an_output_directory_outside_the_trusted_repository_before_staging()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", "namespace MyApp; public class Widget { public void Run() { } }");
        var (workspace, sln) = project.Build();
        var sourceBefore = await project.ReadSourceAsync("Widget.cs");
        var outputDir = Directory.CreateTempSubdirectory("rt-extract-outside-");
        try
        {
            using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
            {
                await Should.ThrowAsync<IOException>(() => ExecuteAsync(
                    "Widget", "ignored", outputDir.FullName, null, false));
            }

            Directory.EnumerateFileSystemEntries(outputDir.FullName).ShouldBeEmpty();
            (await project.ReadSourceAsync("Widget.cs")).ShouldBe(sourceBefore);
        }
        finally
        {
            outputDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task includes_only_public_members()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
                                             namespace MyApp.Services;

                                             public class AgentService
                                             {
                                                 public string GetAgent(int id) => id.ToString();
                                                 public void DeleteAgent(int id) { }
                                                 private void Helper() { }
                                             }
                                             """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync(
                "AgentService",
                "ignored",
                null,
                null,
                false);
        }

        var content = await project.ReadSourceAsync("IAgentService.cs");
        content.ShouldContain("GetAgent", Case.Sensitive);
        content.ShouldContain("DeleteAgent", Case.Sensitive);
        content.ShouldNotContain("Helper", Case.Sensitive);
    }

    [Fact]
    public async Task updates_the_class_to_implement_the_interface()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
                                             namespace MyApp.Services;

                                             public class AgentService
                                             {
                                                 public string GetAgent(int id) => id.ToString();
                                                 public void DeleteAgent(int id) { }
                                                 private void Helper() { }
                                             }
                                             """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync(
                "AgentService",
                "ignored",
                null,
                null,
                false);
        }

        var content = await project.ReadSourceAsync("AgentService.cs");
        content.ShouldContain(": IAgentService", Case.Sensitive);
    }

    [Fact]
    public async Task does_not_write_the_interface_file()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
                                             namespace MyApp.Services;

                                             public class AgentService
                                             {
                                                 public string GetAgent(int id) => id.ToString();
                                                 public void DeleteAgent(int id) { }
                                                 private void Helper() { }
                                             }
                                             """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync(
                "AgentService",
                "ignored",
                null,
                null,
                true);
        }

        Assert.False(project.SourceExists("IAgentService.cs"),
            "IAgentService.cs should NOT be written during dry-run");
    }

    [Fact]
    public async Task rejects_non_class_types()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Point.cs", """
                                      namespace MyApp;
                                      public struct Point { public int X; public int Y; }
                                      """);
        var (_, sln) = project.Build();

        var result = await new ExtractInterfaceService(new SymbolResolutionService())
            .PrepareAsync(sln, "Point", null, null);

        var error = GetError(result);
        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("must be a class", Case.Sensitive);
    }

    [Fact]
    public async Task appends_to_existing_base_list()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp;
                                       public interface IBase { }
                                       public class Widget : IBase
                                       {
                                           public void Run() { }
                                       }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Widget", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("Widget.cs");
        content.ShouldContain("IBase", Case.Sensitive);
        content.ShouldContain("IWidget", Case.Sensitive);
    }

    [Fact]
    public async Task formats_appended_base_list_with_comma_separator()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp;
                                       public interface IBase { }
                                       public class Widget : IBase
                                       {
                                           public void Run() { }
                                       }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Widget", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("Widget.cs");
        content.ShouldContain(", IWidget", Case.Sensitive);
        content.ShouldNotContain("IWidget{", Case.Sensitive);
        content.ShouldNotContain(",IWidget", Case.Sensitive);
    }

    [Fact]
    public async Task internal_class_yields_internal_interface()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Secret.cs", """
                                       namespace MyApp;
                                       internal sealed class Secret
                                       {
                                           public void Run() { }
                                       }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Secret", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("ISecret.cs");
        content.ShouldContain("internal interface ISecret", Case.Sensitive);
        content.ShouldNotContain("public interface", Case.Sensitive);
    }

    [Fact]
    public async Task internal_class_includes_internal_members()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Secret.cs", """
                                       namespace MyApp;
                                       internal sealed class Secret
                                       {
                                           public void PublicRun() { }
                                           internal void InternalRun() { }
                                           internal int InternalCount { get; set; }
                                           internal int ReadOnlyView { get; private set; }
                                           private void Hidden() { }
                                       }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Secret", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("ISecret.cs");
        content.ShouldContain("PublicRun", Case.Sensitive);
        content.ShouldContain("InternalRun", Case.Sensitive);
        content.ShouldContain("int InternalCount { get; set; }", Case.Sensitive);
        // A private setter on an internal property is below the visibility cut: get-only in the interface.
        content.ShouldContain("int ReadOnlyView { get; }", Case.Sensitive);
        content.ShouldNotContain("ReadOnlyView { get; set; }", Case.Sensitive);
        content.ShouldNotContain("Hidden", Case.Sensitive);
    }

    [Fact]
    public async Task includes_public_properties_in_the_interface()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Repo.cs", """
                                     namespace MyApp;
                                     public class Repo
                                     {
                                         public string Name { get; set; } = "";
                                         public int Count { get; } = 0;
                                         private string Secret { get; set; } = "";
                                     }
                                     """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Repo", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("IRepo.cs");
        content.ShouldContain("Name { get; set; }", Case.Sensitive);
        // Count is get-only: a missing setter must not be emitted as a settable accessor.
        content.ShouldContain("Count { get; }", Case.Sensitive);
        content.ShouldNotContain("Count { get; set; }", Case.Sensitive);
        content.ShouldNotContain("Secret", Case.Sensitive);
    }

    [Fact]
    public async Task uses_custom_namespace_override()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp;
                                       public class Widget
                                       {
                                           public void Run() { }
                                       }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Widget", "ignored", null, "MyApp.Contracts", false);
        }

        var content = await project.ReadSourceAsync("IWidget.cs");
        content.ShouldContain("namespace MyApp.Contracts;", Case.Sensitive);
    }

    [Fact]
    public async Task includes_method_parameters_in_the_interface()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
                                           namespace MyApp;
                                           public class Calculator
                                           {
                                               public int Add(int a, int b) => a + b;
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Calculator", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("ICalculator.cs");
        content.ShouldContain("int Add(int a, int b)", Case.Sensitive);
    }

    [Fact]
    public async Task returns_typed_not_found_when_symbol_is_not_a_named_type()
    {
        // A real class exists in the solution, but the resolver returns its typed not-found result.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace MyApp;
                                    public class Foo { public void Run() { } }
                                    """);
        var (_, sln) = project.Build();

        var service = new ExtractInterfaceService(new StubSymbolResolutionService(_ => null));

        var error = GetError(await service.PrepareAsync(sln, "Foo", null, null));
        error.Kind.ShouldBe(RtErrorKind.NotFound);
        error.Message.ShouldContain("No symbol named 'Foo'", Case.Sensitive);
    }

    [Fact]
    public async Task constructor_uses_injected_resolver_not_a_fresh_one()
    {
        // The stub returns not-found while the concrete resolver would find Foo, proving preparation
        // uses the injected resolver.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace MyApp;
                                    public class Foo { public void Run() { } }
                                    """);
        var (_, sln) = project.Build();

        var service = new ExtractInterfaceService(new StubSymbolResolutionService(_ => null));

        GetError(await service.PrepareAsync(sln, "Foo", null, null))
            .Message.ShouldContain("No symbol named 'Foo'", Case.Sensitive);
    }

    [Fact]
    public async Task resolves_type_kind_to_disambiguate_same_named_members()
    {
        // Kills L49 String mutation: the literal "type" kind passed to FindSymbolAsync. With "type"
        // the resolver filters to the class only and extraction succeeds. Mutated to "" the filter
        // widens to All, the property named `Widget` also matches, and the resolver throws on the
        // ambiguity (no selection in a non-interactive test) — so success proves "type" is intact.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp;
                                       public class Widget { public void Run() { } }
                                       public class Holder { public int Widget { get; set; } }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Widget", "ignored", null, null, false);
        }

        Assert.True(project.SourceExists("IWidget.cs"),
            "Extraction must succeed: 'type' kind disambiguates the class from the same-named property.");
    }

    [Fact]
    public async Task falls_back_to_containing_namespace_when_none_supplied()
    {
        // Kills L56 null-coalescing (both directions) and the namespace String mutant: with ns=null
        // the interface namespace must come from the class's containing namespace.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp.Services;
                                       public class Widget { public void Run() { } }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Widget", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("IWidget.cs");
        content.ShouldContain("namespace MyApp.Services;", Case.Sensitive);
    }

    [Fact]
    public async Task writes_interface_to_explicit_output_directory()
    {
        // Kills L66 null-coalescing (remove-left / left-to-right): when outputDir is supplied the
        // generated interface document must live under it, not next to the class file.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp;
                                       public class Widget { public void Run() { } }
                                       """);
        var (workspace, sln) = project.Build();

        var outputDir = Path.Combine(project.Directory, "Contracts");
        var service = new ExtractInterfaceService(new SymbolResolutionService());

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            var preparation = (await service.PrepareAsync(sln, "Widget", outputDir, null)).Unwrap();
            var addedDoc = AddedDocument(preparation.Solution, "IWidget.cs");
            Path.GetDirectoryName(addedDoc.FilePath).ShouldBe(outputDir);
        }
    }

    [Fact]
    public async Task returns_typed_rejection_when_type_has_no_source_location()
    {
        // A metadata-only type has no source location, so preparation must return the actionable
        // source-path rejection rather than leaking a LINQ exception.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Anchor.cs", """
                                       namespace MyApp;
                                       public class Anchor { public void Run() { } }
                                       """);
        var (workspace, sln) = project.Build();

        var compilation = await sln.Projects.First().GetCompilationAsync();
        var metadataType = compilation!.GetSpecialType(SpecialType.System_String);

        var service = new ExtractInterfaceService(new StubSymbolResolutionService(_ => metadataType));

        GetError(await service.PrepareAsync(sln, "String", null, null))
            .Message.ShouldContain("Cannot determine source file", Case.Sensitive);
    }

    [Fact]
    public async Task succeeds_when_project_has_multiple_documents()
    {
        // Kills L106 Linq (Any->All) on the owning-project lookup: with two source files the project
        // does NOT have ALL documents matching the class file path, so an All() mutant would fail to
        // find the owning project and throw "Cannot find project containing the class". Success here
        // proves Any() is intact.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp;
                                       public class Widget { public void Run() { } }
                                       """);
        project.AddSource("Other.cs", """
                                      namespace MyApp;
                                      public class Other { public void Go() { } }
                                      """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Widget", "ignored", null, null, false);
        }

        Assert.True(project.SourceExists("IWidget.cs"),
            "Extraction must succeed even when the project contains other source files.");
    }

    private async Task ExecuteAsync(
        string type,
        string? solution,
        string? outputDir,
        string? ns,
        bool dryRun)
    {
        var result = await _services.Resolve<ExtractInterfaceOperation>().ExecuteAsync(
            new ExtractInterfaceRequest(type, outputDir, ns, solution, dryRun));
        result.Unwrap();
    }

    private static RtError GetError(Result<ExtractInterfaceService.Preparation, RtError> result)
    {
        return result.Match(
            _ => throw new ShouldAssertException("Expected interface extraction to fail."),
            error => error);
    }

    private static Document AddedDocument(Solution solution, string fileName)
    {
        var doc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.Name, fileName, StringComparison.Ordinal));
        doc.ShouldNotBeNull();
        return doc;
    }
}

/// <summary>
///     A minimal <see cref="ISymbolResolutionService" /> whose <c>FindSymbolAsync</c> returns a
///     caller-supplied symbol (or null). All other members are unused by the extraction path and
///     throw if called, so a test accidentally relying on them fails loudly.
/// </summary>
internal sealed class StubSymbolResolutionService : ISymbolResolutionService
{
    private readonly Func<string, ISymbol?> _resolve;

    public StubSymbolResolutionService(Func<string, ISymbol?> resolve) => _resolve = resolve;

    public Task<Result<ISymbol, RtError>> TryFindSymbolAsync(Solution solution, string symbolName, string? kind) =>
        Task.FromResult(_resolve(symbolName) is { } symbol
            ? Result<ISymbol, RtError>.Ok(symbol)
            : Result<ISymbol, RtError>.Err(RtError.NotFound(
                $"No symbol named '{symbolName}' found in solution" +
                (kind != null ? $" (kind: {kind})" : "") + ".")));

    public Task<Result<ISymbol, RtError>> TryFindSymbolByFqnAsync(Solution solution, string fullyQualifiedName, string? kind) =>
        throw new NotImplementedException();

    public Task<Result<ISymbol, RtError>> TryFindMemberBySignatureAsync(
        Solution solution, string signature, bool includeSuggestions = true) =>
        throw new NotImplementedException();

    public Task<ISymbol> FindSymbolAsync(Solution solution, string symbolName, string? kind) =>
        Task.FromResult(_resolve(symbolName)!);

    public Task<ISymbol> FindSymbolByFqnAsync(Solution solution, string fullyQualifiedName, string? kind) =>
        throw new NotImplementedException();

    public Task<ISymbol> FindTypeByFqnAsync(Solution solution, string fqn) =>
        throw new NotImplementedException();

    public Task<IMethodSymbol> FindMethodBySpecAsync(Solution solution, string methodSpec) =>
        throw new NotImplementedException();

    public Task<IMethodSymbol?> FindMethodInTypeAsync(Solution solution, string typeName, string methodName) =>
        throw new NotImplementedException();
}
