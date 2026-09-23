using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class OutlineOperationTypedTests
{
    [Fact]
    public async Task summary_mode_preserves_typed_entries()
    {
        using var project = new AdhocProjectBuilder("OutlineApp");
        project.AddSource("Order.cs", """
            namespace OutlineApp;
            /// <summary>an order</summary>
            public class Order { public void Cancel() { } }
            """);
        var (workspace, solution) = project.Build();
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["OutlineApp"]);
        using (workspace)
        {
            var operation = new OutlineOperation(new FixedSessionFactory(workspace, solution), new OutlineService());
            var result = await operation.ExecuteAsync(new OutlineRequest(Path.Combine(project.ProjectDir, "Order.cs"), Summary: true));
            var envelope = result.Match(value => value, error => throw new InvalidOperationException(error.Message));
            envelope.SchemaVersion.ShouldBe("rt.result.v1");
            envelope.Operation.ShouldBe("outline");
            envelope.Outcome.ShouldBe("Succeeded");
            envelope.Workspace.ManifestState.ShouldBe(nameof(ManifestDiscoveryState.Closed));
            envelope.Workspace.Evaluator.ShouldNotBeNull();
            envelope.Workspace.Inputs.ShouldNotBeNull().ShouldNotBeEmpty();
            var sourceInput = envelope.Workspace.Inputs.Single(input =>
                input.Kind == nameof(SemanticInputKind.SourceDocument));
            sourceInput.Path.ShouldEndWith("Order.cs", Case.Sensitive);
            sourceInput.ExpectedFingerprint.ShouldNotBeNullOrWhiteSpace();
            sourceInput.ObservedFingerprint.ShouldBe(sourceInput.ExpectedFingerprint);
            var summary = envelope.Data.Summaries!.ShouldHaveSingleItem();
            summary.Fqn.ShouldContain("Order");
            summary.Kind.ShouldBe("Class");
            summary.LineFrom.ShouldBeGreaterThan(0);
            summary.LineTo.ShouldBeGreaterThanOrEqualTo(summary.LineFrom);
            summary.Description.ShouldBe("an order");
        }
    }

    [Fact]
    public async Task normal_mode_preserves_outline_type_entries_and_definition_shape()
    {
        using var project = new AdhocProjectBuilder("OutlineApp");
        project.AddSource("Order.cs", "namespace OutlineApp; public class Order { public void Cancel() { } }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new OutlineOperation(new FixedSessionFactory(workspace, solution), new OutlineService());
            var result = await operation.ExecuteAsync(new OutlineRequest(Path.Combine(project.ProjectDir, "Order.cs")));
            var envelope = result.Match(value => value, error => throw new InvalidOperationException(error.Message));
            envelope.Data.Types!
                .ShouldContain(type => type.Name == "Order");
            envelope.Paging.TotalItems.ShouldBe(1);
            envelope.Paging.Truncated.ShouldBeFalse();
            operation.Definition.ResponseType.ShouldBe(typeof(RtResultEnvelope<OutlineResult>));
        }
    }

    [Fact]
    public async Task relative_file_target_resolves_from_the_solution_directory()
    {
        using var fixture = new AdhocProjectBuilder("OutlineApp");
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(fixture.Directory, "src"));
        var sourcePath = Path.Combine(sourceDirectory.FullName, "Order.cs");
        const string source = "namespace OutlineApp; public class Order { }";
        File.WriteAllText(sourcePath, source);
        using var workspace = new AdhocWorkspace();
        workspace.AddSolution(SolutionInfo.Create(
            SolutionId.CreateNewId(),
            VersionStamp.Create(),
            filePath: Path.Combine(fixture.Directory, "OutlineApp.slnx")));
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "OutlineApp",
                "OutlineApp",
                LanguageNames.CSharp,
                filePath: Path.Combine(fixture.Directory, "OutlineApp.csproj")))
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Order.cs",
                SourceText.From(source),
                filePath: sourcePath);
        var operation = new OutlineOperation(
            new FixedSessionFactory(workspace, solution),
            new OutlineService());

        var result = await operation.ExecuteAsync(
            new OutlineRequest(Path.Combine("src", "Order.cs")));

        result.Unwrap().Data.Types!
            .ShouldContain(type => type.Name == "Order");
    }

    [Theory]
    [InlineData("*.cs", "RootType")]
    [InlineData("src", "NestedType")]
    public async Task relative_path_shaped_targets_resolve_from_the_solution_directory(
        string target,
        string expectedType)
    {
        using var fixture = new AdhocProjectBuilder("OutlineApp");
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(fixture.Directory, "src"));
        var rootSourcePath = Path.Combine(fixture.Directory, "Root.cs");
        var nestedSourcePath = Path.Combine(sourceDirectory.FullName, "Nested.cs");
        File.WriteAllText(rootSourcePath, "public class RootType { }");
        File.WriteAllText(nestedSourcePath, "public class NestedType { }");
        using var workspace = new AdhocWorkspace();
        workspace.AddSolution(SolutionInfo.Create(
            SolutionId.CreateNewId(),
            VersionStamp.Create(),
            filePath: Path.Combine(fixture.Directory, "OutlineApp.slnx")));
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "OutlineApp",
                "OutlineApp",
                LanguageNames.CSharp,
                filePath: Path.Combine(fixture.Directory, "OutlineApp.csproj")))
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Root.cs",
                SourceText.From(File.ReadAllText(rootSourcePath)),
                filePath: rootSourcePath)
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Nested.cs",
                SourceText.From(File.ReadAllText(nestedSourcePath)),
                filePath: nestedSourcePath);
        var operation = new OutlineOperation(
            new FixedSessionFactory(workspace, solution),
            new OutlineService());

        var result = await operation.ExecuteAsync(new OutlineRequest(target));

        result.Unwrap().Data.Types!
            .ShouldContain(type => type.Name == expectedType);
    }

    [Fact]
    public async Task partial_workspace_is_visible_and_reduces_confidence()
    {
        using var project = new AdhocProjectBuilder("PartialOutlineApp");
        project.AddSource(
            "Broken.cs",
            "namespace PartialOutlineApp; public class Broken { MissingType Value = null!; }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new OutlineOperation(
                new FixedSessionFactory(workspace, solution),
                new OutlineService());

            var result = await operation.ExecuteAsync(
                new OutlineRequest(Path.Combine(project.ProjectDir, "Broken.cs")));
            var envelope = result.Unwrap();

            envelope.Outcome.ShouldBe("Partial");
            envelope.Workspace.State.ShouldBe("Partial");
            envelope.Confidence.Level.ShouldBe("Limited");
            envelope.Diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains(
                "CS0246",
                StringComparison.Ordinal));
            envelope.Limitations.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public async Task structured_diagnostics_preserve_compiler_severity_and_impact()
    {
        using var project = new AdhocProjectBuilder("WarningOutlineApp");
        project.AddSource(
            "Warning.cs",
            "using System.Text; namespace WarningOutlineApp; public class Warning { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new OutlineOperation(
                new FixedSessionFactory(workspace, solution),
                new OutlineService());

            var envelope = (await operation.ExecuteAsync(
                new OutlineRequest(Path.Combine(project.ProjectDir, "Warning.cs")))).Unwrap();
            var diagnostic = envelope.Diagnostics.Single(item => item.Code == "CS8019");

            diagnostic.Severity.ShouldBe(nameof(DiagnosticSeverity.Hidden));
            diagnostic.Context.ShouldNotBeNull()["completenessImpact"]
                .ShouldBe(nameof(CompletenessDiagnosticImpact.None));
        }
    }

    [Fact]
    public async Task output_is_bounded_and_reports_truthful_paging_metadata()
    {
        using var project = new AdhocProjectBuilder("LargeOutlineApp");
        var declarations = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 105).Select(index => $"public class Type{index} {{ }}"));
        project.AddSource("Many.cs", declarations);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new OutlineOperation(
                new FixedSessionFactory(workspace, solution),
                new OutlineService());

            var result = await operation.ExecuteAsync(
                new OutlineRequest(Path.Combine(project.ProjectDir, "Many.cs")));
            var envelope = result.Unwrap();

            envelope.Paging.TotalItems.ShouldBe(105);
            envelope.Paging.ReturnedItems.ShouldBe(100);
            envelope.Paging.Truncated.ShouldBeTrue();
            envelope.Paging.ContinuationToken.ShouldBeNull();
            envelope.Data.Types!.Count.ShouldBe(100);
            envelope.Limitations.ShouldContain(limitation => limitation.Contains(
                "item limit",
                StringComparison.Ordinal));
        }
    }

    private sealed class FixedSessionFactory(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);
        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) => Task.FromResult((workspace, solution));
    }
}
