using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ExtractClass : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ExtractClass(RtServicesFixture services) => _services = services;

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

    private async Task<CommandCapture> RunExtractClassAsync(
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
            await _services.Resolve<ExtractClassCommand>().ExecuteAsync(
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
    public async Task creates_new_file()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractClassCommand>().ExecuteAsync(
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
    public async Task removes_the_extracted_members_from_the_source_type()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractClassCommand>().ExecuteAsync(
                "BigService",
                "LogInfo",
                "LogHelper",
                false,
                "ignored",
                null,
                false);
        }

        var content = await project.ReadSourceAsync("BigService.cs");
        content.ShouldNotContain("void LogInfo", Case.Sensitive);
    }

    [Fact]
    public async Task injects_a_field_for_the_extracted_type()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractClassCommand>().ExecuteAsync(
                "BigService",
                "LogInfo",
                "LogHelper",
                false,
                "ignored",
                null,
                false);
        }

        var content = await project.ReadSourceAsync("BigService.cs");
        content.ShouldContain("private readonly LogHelper _logHelper", Case.Sensitive);
    }

    [Fact]
    public async Task avoids_injecting_the_extracted_type()
    {
        using var project = BuildFixture();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractClassCommand>().ExecuteAsync(
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

        newClassContent.ShouldContain("static class LogHelper", Case.Sensitive);
        sourceContent.ShouldNotContain("_logHelper", Case.Sensitive);
    }

    [Fact]
    public async Task does_not_write_files()
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
        afterContent.ShouldBe(originalContent);
        capture.StdOut.ShouldContain("LogHelper.cs (new file)", Case.Sensitive);
        capture.StdOut.ShouldContain("private readonly LogHelper _logHelper", Case.Sensitive);
    }

    [Fact]
    public async Task refuses_ambiguous_source_type_names()
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

        capture.ExitCode.ShouldBe(1);
        Assert.Contains("ambiguous", capture.StdErr, StringComparison.OrdinalIgnoreCase);
        capture.StdErr.ShouldContain("Billing.SharedService", Case.Sensitive);
        capture.StdErr.ShouldContain("Shipping.SharedService", Case.Sensitive);
        project.SourceExists("LogHelper.cs").ShouldBeFalse();
        (await project.ReadSourceAsync("Billing/SharedService.cs")).ShouldBe(billingBefore);
        (await project.ReadSourceAsync("Shipping/SharedService.cs")).ShouldBe(shippingBefore);
    }

    [Fact]
    public async Task refuses_extraction_that_captures_non_extracted_instance_state()
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

        capture.ExitCode.ShouldBe(1);
        capture.StdErr.ShouldContain("_baseUrl", Case.Sensitive);
        Assert.Contains("extract", capture.StdErr, StringComparison.OrdinalIgnoreCase);
        project.SourceExists("LogHelper.cs").ShouldBeFalse();
        (await project.ReadSourceAsync("BigService.cs")).ShouldBe(before);
    }

    [Fact]
    public async Task refuses_partial_extraction_from_multi_variable_field_declarations()
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

        capture.ExitCode.ShouldBe(1);
        Assert.Contains("multiple variables", capture.StdErr, StringComparison.OrdinalIgnoreCase);
        capture.StdErr.ShouldContain("a", Case.Sensitive);
        project.SourceExists("ValueHolder.cs").ShouldBeFalse();
        (await project.ReadSourceAsync("BigService.cs")).ShouldBe(before);
    }

    [Fact]
    public async Task extracts_self_contained_state_when_all_required_members_are_selected()
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
            await _services.Resolve<ExtractClassCommand>().ExecuteAsync(
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

        extractedContent.ShouldContain("class PathFormatter", Case.Sensitive);
        extractedContent.ShouldContain("private string _prefix = \"https://api\";", Case.Sensitive);
        extractedContent.ShouldContain("string FormatPath", Case.Sensitive);
        sourceContent.ShouldNotContain("_prefix = \"https://api\"", Case.Sensitive);
        sourceContent.ShouldNotContain("FormatPath", Case.Sensitive);
        sourceContent.ShouldContain("private readonly PathFormatter _pathFormatter", Case.Sensitive);
    }

    [Fact]
    public async Task reports_an_error_when_source_class_is_not_found()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;
                                           public class BigService { public void Go() { } }
                                           """);
        var (workspace, sln) = project.Build();
        var before = await project.ReadSourceAsync("BigService.cs");

        CommandCapture capture;
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            capture = await RunExtractClassAsync("Missing", "Go", "Helper", false, "ignored", null, false);
        }

        capture.ExitCode.ShouldBe(1);
        capture.StdErr.ShouldContain("Missing", Case.Sensitive);
        (await project.ReadSourceAsync("BigService.cs")).ShouldBe(before);
    }

    [Fact]
    public async Task reports_an_error_when_no_specified_members_exist()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;
                                           public class BigService { public void Go() { } }
                                           """);
        var (workspace, sln) = project.Build();
        var before = await project.ReadSourceAsync("BigService.cs");

        CommandCapture capture;
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            capture = await RunExtractClassAsync("BigService", "Nonexistent", "Helper", true, "ignored", null, false);
        }

        capture.ExitCode.ShouldBe(1);
        capture.StdErr.ShouldContain("Nonexistent", Case.Sensitive);
        project.SourceExists("Helper.cs").ShouldBeFalse();
        (await project.ReadSourceAsync("BigService.cs")).ShouldBe(before);
    }

    [Fact]
    public async Task reports_an_error_when_only_some_specified_members_are_missing()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;
                                           public class BigService { public void Go() { } }
                                           """);
        var (workspace, sln) = project.Build();
        var before = await project.ReadSourceAsync("BigService.cs");

        CommandCapture capture;
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            capture = await RunExtractClassAsync("BigService", "Go,Missing", "Helper", true, "ignored", null, false);
        }

        capture.ExitCode.ShouldBe(1);
        capture.StdErr.ShouldContain("Missing", Case.Sensitive);
        project.SourceExists("Helper.cs").ShouldBeFalse();
        (await project.ReadSourceAsync("BigService.cs")).ShouldBe(before);
    }

    [Fact]
    public async Task refuses_extraction_when_selected_member_uses_this_directly()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;
                                           public class BigService
                                           {
                                               public BigService Clone() => this;
                                           }
                                           """);
        var (workspace, sln) = project.Build();
        var before = await project.ReadSourceAsync("BigService.cs");

        CommandCapture capture;
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            capture = await RunExtractClassAsync("BigService", "Clone", "Helper", false, "ignored", null, false);
        }

        capture.ExitCode.ShouldBe(1);
        capture.StdErr.ShouldContain("this", Case.Sensitive);
        project.SourceExists("Helper.cs").ShouldBeFalse();
        (await project.ReadSourceAsync("BigService.cs")).ShouldBe(before);
    }

    [Fact]
    public async Task extracts_a_property_to_the_new_class()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;
                                           public class BigService
                                           {
                                               public string Version { get; set; } = "1.0";
                                               public void Process() { }
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ExtractClassCommand>().ExecuteAsync("BigService", "Version", "VersionInfo", true, "ignored", null,
                false);
        }

        var extracted = await project.ReadSourceAsync("VersionInfo.cs");
        var source = await project.ReadSourceAsync("BigService.cs");
        extracted.ShouldContain("string Version", Case.Sensitive);
        source.ShouldNotContain("string Version", Case.Sensitive);
    }

    private sealed record CommandCapture(string StdOut, string StdErr, int ExitCode);
}
