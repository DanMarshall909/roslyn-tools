using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public sealed class GeneratedTestStubs
{
    [Fact]
    public async Task add_a_single_fact_to_the_selected_class()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("OrderTests.cs", """
                                           namespace TestApp;

                                           public class OrderTests
                                           {
                                           }
                                           """);
        project.AddSource("InvoiceTests.cs", """
                                             namespace TestApp;

                                             public class InvoiceTests
                                             {
                                             }
                                             """);

        var (_, solution) = project.Build();
        var targetFilePath = Path.Combine(project.ProjectDir, "OrderTests.cs");
        var writer = new CapturingSolutionChangeWriter();

        await new AddTestService(NullLogger<AddTestService>.Instance, writer).RunAsync(
            solution,
            "OrderTests",
            "returns_total_for_valid_items",
            null,
            null,
            targetFilePath,
            false);

        var orderText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "OrderTests.cs");
        var invoiceText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "InvoiceTests.cs");

        Assert.Contains("using System;", orderText);
        Assert.Contains("using Xunit;", orderText);
        Assert.Contains("[Fact]", orderText);
        Assert.Contains("public void returns_total_for_valid_items()", orderText);
        Assert.Contains("throw new NotImplementedException();", orderText);
        Assert.DoesNotContain("returns_total_for_valid_items", invoiceText);
        Assert.False(writer.DryRun);
    }

    [Fact]
    public async Task group_acceptance_criteria_inside_the_named_context()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("OrderTests.cs", """
                                           namespace TestApp;

                                           public class OrderTests
                                           {
                                           }
                                           """);

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new AddTestService(NullLogger<AddTestService>.Instance, writer).RunAsync(
            solution,
            "OrderTests",
            null,
            "returns total for valid items; rejects missing customer",
            "WhenCheckingOut",
            null,
            false);

        var orderText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "OrderTests.cs");
        var outerClassIndex = orderText.IndexOf("public class OrderTests", StringComparison.Ordinal);
        var nestedClassIndex = orderText.IndexOf("public class WhenCheckingOut", StringComparison.Ordinal);

        Assert.True(outerClassIndex >= 0);
        Assert.True(nestedClassIndex > outerClassIndex);
        Assert.Contains("public void Returns_Total_For_Valid_Items()", orderText);
        Assert.Contains("public void Rejects_Missing_Customer()", orderText);
    }

    [Fact]
    public async Task leave_the_solution_unchanged_when_the_class_is_absent()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("OrderTests.cs", """
                                           namespace TestApp;

                                           public class OrderTests
                                           {
                                           }
                                           """);

        var (_, solution) = project.Build();
        var originalExitCode = Environment.ExitCode;
        var writer = new CapturingSolutionChangeWriter();

        try
        {
            Environment.ExitCode = 0;

            await new AddTestService(NullLogger<AddTestService>.Instance, writer).RunAsync(
                solution,
                "MissingTests",
                "returns_total_for_valid_items",
                null,
                null,
                null,
                false);

            Assert.Null(writer.Solution);
            Assert.Equal(1, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
        }
    }

    [Fact]
    public async Task add_command_scenarios_to_the_existing_command_file()
    {
        var testFilePath = Path.GetFullPath("tests/RoslynTools.Tests/Refactor/Rename.cs");
        var documentId = DocumentId.CreateNewId(ProjectId.CreateNewId());
        var projectInfo = ProjectInfo.Create(
            documentId.ProjectId,
            VersionStamp.Create(),
            "RoslynTools.Tests",
            "RoslynTools.Tests",
            LanguageNames.CSharp,
            documents:
            [
                DocumentInfo.Create(
                    documentId,
                    "Rename.cs",
                    filePath: testFilePath,
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From("""
                        namespace RoslynTools.Tests.Refactor;

                        public class Rename
                        {
                        }
                        """), VersionStamp.Create(), testFilePath)))
            ]);
        var workspace = new AdhocWorkspace();
        workspace.AddProject(projectInfo);
        var writer = new CapturingSolutionChangeWriter();
        var commandInfo = new RtCommandInfo(
            "rename",
            true,
            false,
            "refactor",
            "Scaffold rename command tests.",
            []);

        await new AddTestService(NullLogger<AddTestService>.Instance, writer).RunCommandBasedAsync(
            workspace.CurrentSolution,
            commandInfo,
            "source file owns the call site",
            "updates the reference",
            "SourceFileCallSite",
            null,
            null,
            false);

        var renameText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Rename.cs");

        Assert.Contains("using System;", renameText);
        Assert.Contains("using Xunit;", renameText);
        Assert.Contains("[Trait(\"Concern\", \"SourceFileCallSite\")]", renameText);
        Assert.Contains("public async Task with_source_file_owns_the_call_site_updates_the_reference()", renameText);
        Assert.Contains("throw new NotImplementedException();", renameText);
    }

}
