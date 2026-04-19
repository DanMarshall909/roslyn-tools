using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class ExtractClassTests
{
    private static AdhocProjectBuilder BuildFixture()
    {
        var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
            namespace MyApp;

            public class BigService
            {
                private readonly string _baseUrl;

                public BigService(string baseUrl)
                {
                    _baseUrl = baseUrl;
                }

                public string FetchData(int id) => $"{_baseUrl}/{id}";
                public void ProcessData(string data) { /* ... */ }
                public void LogInfo(string msg) { }
            }
            """);
        return project;
    }

    [Fact]
    public async Task Creates_new_file()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ExtractClassCommand.ExecuteAsync(
            typeName: "BigService",
            members: "LogInfo",
            newTypeName: "LogHelper",
            isStatic: false,
            solutionPath: "ignored",
            outputDir: null,
            dryRun: false);

        Assert.True(project.SourceExists("LogHelper.cs"),
            "LogHelper.cs should have been created alongside BigService.cs");
    }

    [Fact]
    public async Task Removes_the_extracted_members_from_the_source_type()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ExtractClassCommand.ExecuteAsync(
            typeName: "BigService",
            members: "LogInfo",
            newTypeName: "LogHelper",
            isStatic: false,
            solutionPath: "ignored",
            outputDir: null,
            dryRun: false);

        var content = await project.ReadSourceAsync("BigService.cs");
        Assert.DoesNotContain("void LogInfo", content);
    }

    [Fact]
    public async Task Injects_a_field_for_the_extracted_type()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ExtractClassCommand.ExecuteAsync(
            typeName: "BigService",
            members: "LogInfo",
            newTypeName: "LogHelper",
            isStatic: false,
            solutionPath: "ignored",
            outputDir: null,
            dryRun: false);

        var content = await project.ReadSourceAsync("BigService.cs");
        Assert.Contains("private readonly LogHelper _logHelper", content);
    }

    [Fact]
    public async Task Avoids_injecting_the_extracted_type()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ExtractClassCommand.ExecuteAsync(
            typeName: "BigService",
            members: "LogInfo",
            newTypeName: "LogHelper",
            isStatic: true,
            solutionPath: "ignored",
            outputDir: null,
            dryRun: false);

        var newClassContent = await project.ReadSourceAsync("LogHelper.cs");
        var sourceContent = await project.ReadSourceAsync("BigService.cs");

        Assert.Contains("static class LogHelper", newClassContent);
        Assert.DoesNotContain("_logHelper", sourceContent);
    }

    [Fact]
    public async Task Does_not_write_files()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ExtractClassCommand.ExecuteAsync(
            typeName: "BigService",
            members: "LogInfo",
            newTypeName: "LogHelper",
            isStatic: false,
            solutionPath: "ignored",
            outputDir: null,
            dryRun: true);

        Assert.False(project.SourceExists("LogHelper.cs"),
            "LogHelper.cs should NOT be written during dry-run");
    }
}
