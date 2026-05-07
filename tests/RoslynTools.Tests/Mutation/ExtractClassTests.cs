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

    private static async Task<CommandCapture> RunExtractClassAsync(
        string typeName,
        string members,
        string newTypeName,
        bool isStatic,
        string? solutionPath,
        string? outputDir,
        bool dryRun)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Console.SetOut(stdout);
        Console.SetError(stderr);
        Environment.ExitCode = 0;

        try
        {
            await ExtractClassCommand.ExecuteAsync(
                typeName,
                members,
                newTypeName,
                isStatic,
                solutionPath,
                outputDir,
                dryRun);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var exitCode = Environment.ExitCode;
        Environment.ExitCode = 0;
        return new CommandCapture(stdout.ToString(), stderr.ToString(), exitCode);
    }

    [Fact]
    public async Task Creates_new_file()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExtractClassCommand.ExecuteAsync(
                "BigService",
                "LogInfo",
                "LogHelper",
                false,
                "ignored",
                null,
                false);
        }

        Assert.True(project.SourceExists("LogHelper.cs"),
            "LogHelper.cs should have been created alongside BigService.cs");
    }

    [Fact]
    public async Task Removes_the_extracted_members_from_the_source_type()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExtractClassCommand.ExecuteAsync(
                "BigService",
                "LogInfo",
                "LogHelper",
                false,
                "ignored",
                null,
                false);
        }

        var content = await project.ReadSourceAsync("BigService.cs");
        Assert.DoesNotContain("void LogInfo", content);
    }

    [Fact]
    public async Task Injects_a_field_for_the_extracted_type()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExtractClassCommand.ExecuteAsync(
                "BigService",
                "LogInfo",
                "LogHelper",
                false,
                "ignored",
                null,
                false);
        }

        var content = await project.ReadSourceAsync("BigService.cs");
        Assert.Contains("private readonly LogHelper _logHelper", content);
    }

    [Fact]
    public async Task Avoids_injecting_the_extracted_type()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExtractClassCommand.ExecuteAsync(
                "BigService",
                "LogInfo",
                "LogHelper",
                true,
                "ignored",
                null,
                false);
        }

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
        var originalContent = await project.ReadSourceAsync("BigService.cs");

        CommandCapture capture;

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            capture = await RunExtractClassAsync(
                "BigService",
                "LogInfo",
                "LogHelper",
                false,
                "ignored",
                null,
                true);
        }

        var afterContent = await project.ReadSourceAsync("BigService.cs");
        Assert.False(project.SourceExists("LogHelper.cs"),
            "LogHelper.cs should NOT be written during dry-run");
        Assert.Equal(originalContent, afterContent);
        Assert.Contains("LogHelper.cs (new file)", capture.StdOut);
        Assert.Contains("private readonly LogHelper _logHelper", capture.StdOut);
    }

    [Fact]
    public async Task Refuses_ambiguous_source_type_names()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("Billing/SharedService.cs", """
                                                      namespace Billing;

                                                      public class SharedService
                                                      {
                                                          public void LogInfo() { }
                                                      }
                                                      """);
        project.AddSource("Shipping/SharedService.cs", """
                                                       namespace Shipping;

                                                       public class SharedService
                                                       {
                                                           public void LogInfo() { }
                                                       }
                                                       """);

        var (workspace, sln) = project.Build();
        var billingBefore = await project.ReadSourceAsync("Billing/SharedService.cs");
        var shippingBefore = await project.ReadSourceAsync("Shipping/SharedService.cs");

        CommandCapture capture;
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            capture = await RunExtractClassAsync(
                "SharedService",
                "LogInfo",
                "LogHelper",
                false,
                "ignored",
                null,
                false);
        }

        Assert.Equal(1, capture.ExitCode);
        Assert.Contains("ambiguous", capture.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Billing.SharedService", capture.StdErr);
        Assert.Contains("Shipping.SharedService", capture.StdErr);
        Assert.False(project.SourceExists("LogHelper.cs"));
        Assert.Equal(billingBefore, await project.ReadSourceAsync("Billing/SharedService.cs"));
        Assert.Equal(shippingBefore, await project.ReadSourceAsync("Shipping/SharedService.cs"));
    }

    [Fact]
    public async Task Refuses_extraction_that_captures_non_extracted_instance_state()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;

                                           public class BigService
                                           {
                                               private string _baseUrl = "https://api";

                                               public string LogInfo(int id)
                                               {
                                                   return $"{_baseUrl}/{id}";
                                               }
                                           }
                                           """);

        var (workspace, sln) = project.Build();
        var before = await project.ReadSourceAsync("BigService.cs");

        CommandCapture capture;
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            capture = await RunExtractClassAsync(
                "BigService",
                "LogInfo",
                "LogHelper",
                false,
                "ignored",
                null,
                false);
        }

        Assert.Equal(1, capture.ExitCode);
        Assert.Contains("_baseUrl", capture.StdErr);
        Assert.Contains("extract", capture.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.False(project.SourceExists("LogHelper.cs"));
        Assert.Equal(before, await project.ReadSourceAsync("BigService.cs"));
    }

    [Fact]
    public async Task Refuses_partial_extraction_from_multi_variable_field_declarations()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;

                                           public class BigService
                                           {
                                               private int a, b;
                                           }
                                           """);

        var (workspace, sln) = project.Build();
        var before = await project.ReadSourceAsync("BigService.cs");

        CommandCapture capture;
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            capture = await RunExtractClassAsync(
                "BigService",
                "a",
                "ValueHolder",
                true,
                "ignored",
                null,
                false);
        }

        Assert.Equal(1, capture.ExitCode);
        Assert.Contains("multiple variables", capture.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a", capture.StdErr);
        Assert.False(project.SourceExists("ValueHolder.cs"));
        Assert.Equal(before, await project.ReadSourceAsync("BigService.cs"));
    }

    [Fact]
    public async Task Extracts_self_contained_state_when_all_required_members_are_selected()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;

                                           public class BigService
                                           {
                                               private string _prefix = "https://api";

                                               public string FormatPath(int id)
                                               {
                                                   return $"{_prefix}/{id}";
                                               }

                                               public void KeepWorking() { }
                                           }
                                           """);

        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExtractClassCommand.ExecuteAsync(
                "BigService",
                "_prefix,FormatPath",
                "PathFormatter",
                false,
                "ignored",
                null,
                false);
        }

        var extractedContent = await project.ReadSourceAsync("PathFormatter.cs");
        var sourceContent = await project.ReadSourceAsync("BigService.cs");

        Assert.Contains("class PathFormatter", extractedContent);
        Assert.Contains("private string _prefix = \"https://api\";", extractedContent);
        Assert.Contains("string FormatPath", extractedContent);
        Assert.DoesNotContain("_prefix = \"https://api\"", sourceContent);
        Assert.DoesNotContain("FormatPath", sourceContent);
        Assert.Contains("private readonly PathFormatter _pathFormatter", sourceContent);
    }

    private sealed record CommandCapture(string StdOut, string StdErr, int ExitCode);
}
