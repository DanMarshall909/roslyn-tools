using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Commands;
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync(
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync(
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync(
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync(
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExtractInterfaceService(
                    NullLogger<ExtractInterfaceService>.Instance,
                    new CapturingSolutionChangeWriter())
                .RunAsync(sln, "Point", null, null, false));

        ex.Message.ShouldContain("must be a class", Case.Sensitive);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Widget", "ignored", null, null, false);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Widget", "ignored", null, null, false);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Secret", "ignored", null, null, false);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Secret", "ignored", null, null, false);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Repo", "ignored", null, null, false);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Widget", "ignored", null, "MyApp.Contracts", false);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Calculator", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("ICalculator.cs");
        content.ShouldContain("int Add(int a, int b)", Case.Sensitive);
    }

    [Fact]
    public async Task throws_when_symbol_is_not_a_named_type()
    {
        // A real class exists in the solution, but the resolver hands back null. This pins the
        // null-coalescing throw (L49 remove-right) and its message (L50). If the `?? throw` were
        // removed, `as INamedTypeSymbol` of null would NRE with a different message; if the fake
        // were bypassed (L31 ?? remove-left), the real class would resolve and no throw occurs.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace MyApp;
                                    public class Foo { public void Run() { } }
                                    """);
        var (_, sln) = project.Build();

        var service = new ExtractInterfaceService(
            NullLogger<ExtractInterfaceService>.Instance,
            new CapturingSolutionChangeWriter(),
            new StubSymbolResolutionService(_ => null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync(sln, "Foo", null, null, false));

        ex.Message.ShouldContain("is not a named type", Case.Sensitive);
    }

    [Fact]
    public async Task constructor_uses_injected_resolver_not_a_fresh_one()
    {
        // Kills L31 null-coalescing (remove-left): `_symbols = symbols ?? new SymbolResolutionService()`.
        // If the mutant replaces the expression with `new SymbolResolutionService()`, the real
        // resolver would FIND the class `Foo` and extraction would succeed — but our stub returns
        // null, so the only way this throws "is not a named type" is if the injected stub is used.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace MyApp;
                                    public class Foo { public void Run() { } }
                                    """);
        var (_, sln) = project.Build();

        var service = new ExtractInterfaceService(
            NullLogger<ExtractInterfaceService>.Instance,
            new CapturingSolutionChangeWriter(),
            new StubSymbolResolutionService(_ => null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync(sln, "Foo", null, null, false));
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Widget", "ignored", null, null, false);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Widget", "ignored", null, null, false);
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
        var capturing = new CapturingSolutionChangeWriter();
        var service = new ExtractInterfaceService(
            NullLogger<ExtractInterfaceService>.Instance,
            capturing);

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await service.RunAsync(sln, "Widget", outputDir, null, false);
        }

        var addedDoc = AddedDocument(capturing, "IWidget.cs");
        Path.GetDirectoryName(addedDoc.FilePath).ShouldBe(outputDir);
    }

    [Fact]
    public async Task throws_when_type_has_no_source_location()
    {
        // Kills L58 (FirstOrDefault->First would throw "Sequence contains no elements"), L63
        // statement, and L64 message: a metadata-only type symbol has no in-source location, so the
        // service must throw OUR "Cannot determine source file" message.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Anchor.cs", """
                                       namespace MyApp;
                                       public class Anchor { public void Run() { } }
                                       """);
        var (workspace, sln) = project.Build();

        var compilation = await sln.Projects.First().GetCompilationAsync();
        var metadataType = compilation!.GetSpecialType(SpecialType.System_String);

        var service = new ExtractInterfaceService(
            NullLogger<ExtractInterfaceService>.Instance,
            new CapturingSolutionChangeWriter(),
            new StubSymbolResolutionService(_ => metadataType));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync(sln, "String", null, null, false));

        ex.Message.ShouldContain("Cannot determine source file", Case.Sensitive);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractInterfaceCommand>().ExecuteAsync("Widget", "ignored", null, null, false);
        }

        Assert.True(project.SourceExists("IWidget.cs"),
            "Extraction must succeed even when the project contains other source files.");
    }

    [Fact]
    public async Task logs_the_extraction_with_interface_and_class_names()
    {
        // Kills L117 Statement (drops the RtInfo call) and String (empties the template): a capturing
        // logger must receive a message that names both the interface and the source class.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp;
                                       public class Widget { public void Run() { } }
                                       """);
        var (workspace, sln) = project.Build();

        var logger = new CapturingLogger<ExtractInterfaceService>();
        var service = new ExtractInterfaceService(logger, new CapturingSolutionChangeWriter());

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await service.RunAsync(sln, "Widget", null, null, false);
        }

        logger.Messages.ShouldContain(m => m.Contains("IWidget") && m.Contains("Widget"));
    }

    private static Document AddedDocument(CapturingSolutionChangeWriter capturing, string fileName)
    {
        var doc = capturing.NewSolution!.Projects
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

    public Task<Result<ISymbol, RtError>> TryFindTypeByFqnAsync(Solution solution, string fqn) =>
        throw new NotImplementedException();

    public Task<Result<IMethodSymbol, RtError>> TryFindMethodBySpecAsync(Solution solution, string methodSpec) =>
        throw new NotImplementedException();

    public Task<Result<ISymbol, RtError>> TryFindMemberBySignatureAsync(Solution solution, string signature) =>
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

/// <summary>
///     Captures rendered log messages so tests can assert a diagnostic was emitted with the
///     expected content. Reports <see cref="IsEnabled" /> as true so the production formatter runs.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
