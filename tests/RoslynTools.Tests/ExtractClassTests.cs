using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests;

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
    public async Task ExtractClass_CreatesNewFile()
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
    public async Task ExtractClass_ExtractedMembersAbsentFromSource()
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
    public async Task ExtractClass_SourceReceivesInjectedField()
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
    public async Task ExtractClass_StaticFlag_NoInjection()
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
    public async Task ExtractClass_DryRun_NoWrites()
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
