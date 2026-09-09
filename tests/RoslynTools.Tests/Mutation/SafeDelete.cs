using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

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

    private static async Task<(AdhocProjectBuilder Project, Workspace Workspace, Solution Solution)> BuildFixtureAsync()
    {
        var project = new AdhocProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", SafeAppSource);
        var (workspace, solution) = project.Build();
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["SafeApp"]);
        return (project, workspace, solution);
    }

    [Fact]
    public async Task cli_adapter_deletes_the_type_through_the_typed_operation()
    {
        var (project, workspace, solution) = await BuildFixtureAsync();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var exitCode = await App.RunAsync(
                ["delete-unreferenced", "--symbol", "Orphan", "--solution", "ignored", "--dry-run", "false"]);

            exitCode.ShouldBe(0);
            var content = await project.ReadSourceAsync("SafeApp.cs");
            content.ShouldNotContain("class Orphan", Case.Sensitive);
        }
    }

    [Fact]
    public async Task cli_adapter_returns_non_zero_for_a_referenced_type_without_writing()
    {
        var (project, workspace, solution) = await BuildFixtureAsync();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var exitCode = await App.RunAsync(
                ["delete-unreferenced", "--symbol", "Used", "--solution", "ignored"]);

            exitCode.ShouldBe(1);
            var content = await project.ReadSourceAsync("SafeApp.cs");
            content.ShouldContain("class Used", Case.Sensitive);
        }
    }

    [Fact]
    public async Task rejects_a_referenced_type_with_structured_reference_evidence()
    {
        var (project, workspace, solution) = await BuildFixtureAsync();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var result = await ExecuteResultAsync("Used");

            result.IsErr.ShouldBeTrue();
            var error = result.Match(_ => null!, failure => failure);
            error.Kind.ShouldBe(RtErrorKind.InvalidInput);
            error.Message.ShouldContain("Used", Case.Sensitive);
            error.Message.ShouldContain("new Used", Case.Sensitive);
            error.Context.ShouldNotBeNull();
            error.Context["referenceCount"].ShouldBe("1");
            error.Context["reference.0.filePath"].ShouldEndWith("SafeApp.cs", Case.Sensitive);
            error.Context["reference.0.lineText"].ShouldContain("new Used", Case.Sensitive);

            var content = await project.ReadSourceAsync("SafeApp.cs");
            content.ShouldContain("class Used", Case.Sensitive);
        }
    }

    [Fact]
    public async Task deletes_the_method()
    {
        var (project, workspace, solution) = await BuildFixtureAsync();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var execution = await ExecuteAsync("Svc.Dead", "method");

            execution.Status.ShouldBe(MutationExecutionStatus.Applied);
            execution.Value.ShouldNotBeNull();
            execution.Value.Kind.ShouldBe("method");
            execution.Value.ReferenceCount.ShouldBe(0);
            execution.Value.WorkspaceState.ShouldBe(WorkspaceCompletenessState.Complete);
            execution.Value.Limitations.ShouldContain(limitation =>
                limitation.Contains("external consumers", StringComparison.Ordinal));
            execution.ChangedFiles.Single().ShouldEndWith("SafeApp.cs", Case.Sensitive);
            var content = await project.ReadSourceAsync("SafeApp.cs");
            content.ShouldNotContain("void Dead()", Case.Sensitive);
            content.ShouldContain("void Live()", Case.Sensitive);
        }
    }

    [Fact]
    public async Task unverified_adhoc_workspace_cannot_prepare_a_deletion_even_when_its_source_compiles()
    {
        using var project = new AdhocProjectBuilder("UnverifiedApp");
        project.AddSource("Orphan.cs", "public class Orphan { }");
        var (workspace, solution) = project.Build();
        var compilation = (await solution.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
        compilation.GetDiagnostics().ShouldNotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var result = await ExecuteResultAsync("Orphan", dryRun: true);

            var error = result.Match(_ => null!, failure => failure);
            error.ShouldNotBeNull();
            error.Kind.ShouldBe(RtErrorKind.WorkspaceLoadFailure);
            error.Context!["workspaceState"].ShouldBe("Partial");
            error.Context.Values.ShouldContain(value => value.Contains("Acquisition evidence is absent", StringComparison.Ordinal));
            (await project.ReadSourceAsync("Orphan.cs")).ShouldBe("public class Orphan { }");
        }
    }

    [Fact]
    public async Task refuses_to_plan_from_a_partial_workspace()
    {
        var project = new AdhocProjectBuilder("IncompleteApp");
        project.AddSource("Incomplete.cs", "public class Orphan { MissingType Value = null!; }");
        var (workspace, solution) = project.Build();
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["IncompleteApp"]);
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var result = await ExecuteResultAsync("Orphan", dryRun: true);

            result.IsErr.ShouldBeTrue();
            var error = result.Match(_ => null!, failure => failure);
            error.Kind.ShouldBe(RtErrorKind.WorkspaceLoadFailure);
            error.Message.ShouldContain("Partial workspace", Case.Sensitive);
            error.Context.ShouldNotBeNull();
            error.Context["workspaceState"].ShouldBe("Partial");
            error.Context.Values.ShouldContain(value => value.Contains("CS0246", StringComparison.Ordinal));
            (await project.ReadSourceAsync("Incomplete.cs")).ShouldContain("class Orphan", Case.Sensitive);
        }
    }

    [Fact]
    public async Task rejects_a_referenced_method_without_writing()
    {
        var (project, workspace, solution) = await BuildFixtureAsync();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var result = await ExecuteResultAsync("Svc.Live", "method");

            result.IsErr.ShouldBeTrue();
            var error = result.Match(_ => null!, failure => failure);
            error.Message.ShouldContain("Live", Case.Sensitive);
            error.Context!["referenceCount"].ShouldBe("1");
            var content = await project.ReadSourceAsync("SafeApp.cs");
            content.ShouldContain("void Live()", Case.Sensitive);
        }
    }

    [Fact]
    public async Task dry_run_reports_the_exact_path_without_modifying_files()
    {
        var (project, workspace, solution) = await BuildFixtureAsync();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var originalContent = await project.ReadSourceAsync("SafeApp.cs");

            var execution = await ExecuteAsync("Orphan", dryRun: true);

            execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
            execution.ChangedFiles.Single().ShouldEndWith("SafeApp.cs", Case.Sensitive);
            var afterContent = await project.ReadSourceAsync("SafeApp.cs");
            afterContent.ShouldBe(originalContent);
        }
    }

    [Fact]
    public async Task treats_nameof_references_as_non_references_and_reports_the_ignored_evidence()
    {
        using var project = new AdhocProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", """
                                         public class Logger
                                         {
                                             public void Log(string msg) { }
                                         }

                                         public class Client
                                         {
                                             void Setup()
                                             {
                                                 var name = nameof(Logger);
                                             }
                                         }
                                         """);
        var (workspace, solution) = project.Build();
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["SafeApp"]);

        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var execution = await ExecuteAsync("Logger");

            execution.Status.ShouldBe(MutationExecutionStatus.Applied);
            execution.Value.ShouldNotBeNull();
            execution.Value.IgnoredNameofReferenceCount.ShouldBe(1);
            var content = await project.ReadSourceAsync("SafeApp.cs");
            content.ShouldNotContain("class Logger", Case.Sensitive);
        }
    }

    [Fact]
    public async Task deletes_a_simple_member_name_after_type_lookup_falls_back()
    {
        using var project = new AdhocProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", """
                                         public class Svc
                                         {
                                             public void Dead() { }
                                             public void Live() { }
                                         }
                                         """);
        var (workspace, solution) = project.Build();
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["SafeApp"]);

        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            await ExecuteAsync("Dead");

            var content = await project.ReadSourceAsync("SafeApp.cs");
            content.ShouldNotContain("void Dead()", Case.Sensitive);
            content.ShouldContain("void Live()", Case.Sensitive);
        }
    }

    [Fact]
    public async Task returns_a_typed_not_found_error_for_an_unknown_symbol()
    {
        var (project, workspace, solution) = await BuildFixtureAsync();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var result = await ExecuteResultAsync("MissingType");

            result.IsErr.ShouldBeTrue();
            var error = result.Match(_ => null!, failure => failure);
            error.Kind.ShouldBe(RtErrorKind.NotFound);
            error.Message.ShouldContain("MissingType", Case.Sensitive);
        }
    }

    [Fact]
    public async Task rejects_a_blank_symbol_before_loading_a_workspace()
    {
        var result = await _services.Resolve<SafeDeleteOperation>().ExecuteAsync(
            new SafeDeleteRequest(" "));

        result.IsErr.ShouldBeTrue();
        var error = result.Match(_ => null!, failure => failure);
        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("symbol", Case.Insensitive);
    }

    [Fact]
    public async Task cancellation_before_dispatch_cannot_apply_the_deletion()
    {
        var (project, workspace, solution) = await BuildFixtureAsync();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var cancellation = new CancellationTokenSource())
        {
            var originalContent = await project.ReadSourceAsync("SafeApp.cs");
            cancellation.Cancel();

            await Should.ThrowAsync<OperationCanceledException>(() =>
                ExecuteResultAsync("Orphan", cancellationToken: cancellation.Token));

            var afterContent = await project.ReadSourceAsync("SafeApp.cs");
            afterContent.ShouldBe(originalContent);
        }
    }

    [Fact]
    public void supports_all_top_level_member_declarations_that_safe_delete_can_remove()
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

        Assert.All(root.DescendantNodes().OfType<ClassDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        Assert.All(root.DescendantNodes().OfType<InterfaceDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        Assert.All(root.DescendantNodes().OfType<StructDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        Assert.All(root.DescendantNodes().OfType<RecordDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        Assert.All(root.DescendantNodes().OfType<EnumDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        Assert.All(root.DescendantNodes().OfType<DelegateDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        Assert.All(root.DescendantNodes().OfType<MethodDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        Assert.All(root.DescendantNodes().OfType<PropertyDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        Assert.All(root.DescendantNodes().OfType<FieldDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        Assert.All(root.DescendantNodes().OfType<EventDeclarationSyntax>(), node =>
            SafeDeleteService.FindDeclarationNode(node).ShouldBeSameAs(node));
        SafeDeleteService.FindDeclarationNode(root).ShouldBeNull();
    }

    private async Task<MutationExecutionResult<SafeDeleteResult>> ExecuteAsync(
        string symbol,
        string? kind = null,
        bool dryRun = false)
    {
        var result = await ExecuteResultAsync(symbol, kind, dryRun);
        return result.Unwrap();
    }

    private Task<Result<MutationExecutionResult<SafeDeleteResult>, RtError>> ExecuteResultAsync(
        string symbol,
        string? kind = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        return _services.Resolve<SafeDeleteOperation>().ExecuteAsync(
            new SafeDeleteRequest(symbol, kind, "ignored", dryRun),
            cancellationToken);
    }
}
