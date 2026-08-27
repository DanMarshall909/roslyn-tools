using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class AddXmlDocOperationTests
{
    [Fact]
    public async Task dry_run_reports_the_changed_file_without_writing()
    {
        using var project = new AdhocProjectBuilder("DocsApp");
        project.AddSource("Widget.cs", "namespace DocsApp; public class Widget { }");
        var (workspace, solution) = project.Build();
        var jsonPath = await WriteDocsAsync(project, new Dictionary<string, string>
        {
            ["DocsApp.Widget"] = "Represents a widget."
        });
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(
            new AddXmlDocRequest(jsonPath, DryRun: true))).Unwrap();

        operation.Definition.Name.ShouldBe("add_xml_doc");
        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.Value!.Applied.ShouldBe(1);
        execution.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("Widget.cs");
        writer.NewSolution.ShouldBeNull("preview must not cross the write boundary");
    }

    [Fact]
    public async Task missing_symbols_are_returned_as_data_and_diagnostics()
    {
        using var project = new AdhocProjectBuilder("DocsApp");
        project.AddSource("Widget.cs", "namespace DocsApp; public class Widget { }");
        var (workspace, solution) = project.Build();
        var jsonPath = await WriteDocsAsync(project, new Dictionary<string, string>
        {
            ["DocsApp.Missing"] = "Missing."
        });
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new AddXmlDocRequest(jsonPath))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.NoOp);
        execution.Value!.SkippedSymbols.ShouldBe(["DocsApp.Missing"]);
        execution.Diagnostics.ShouldHaveSingleItem().Message.ShouldContain("DocsApp.Missing");
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task relative_json_path_uses_the_explicit_working_directory()
    {
        using var project = new AdhocProjectBuilder("DocsApp");
        project.AddSource("Widget.cs", "namespace DocsApp; public class Widget { }");
        var (workspace, solution) = project.Build();
        await WriteDocsAsync(project, new Dictionary<string, string>
        {
            ["DocsApp.Widget"] = "Represents a widget."
        });
        var operation = CreateOperation(
            workspace,
            solution,
            new CapturingSolutionChangeWriter());

        var result = await operation.ExecuteAsync(new AddXmlDocRequest(
            "docs.json",
            DryRun: true,
            WorkingDirectory: project.ProjectDir));

        result.IsOk.ShouldBeTrue(result.Match(_ => string.Empty, error => error.Message));
    }

    private static async Task<string> WriteDocsAsync(
        AdhocProjectBuilder project,
        IReadOnlyDictionary<string, string> docs)
    {
        var path = Path.Combine(project.ProjectDir, "docs.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(docs));
        return path;
    }

    private static AddXmlDocOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer)
    {
        return new AddXmlDocOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new AddXmlDocService(new SymbolResolutionService()),
            writer);
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
