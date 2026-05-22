using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class SolutionWriterIntegration
{
    [Fact]
    public async Task writes_added_documents_to_new_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
                                           namespace TestProject;
                                           public class Calculator
                                           {
                                               public int Add(int a, int b) => a + b;
                                           }
                                           """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var roslynProject = sln.Projects.First();
            var newFilePath = Path.Combine(project.ProjectDir, "NewClass.cs");
            var newDoc = sln.AddDocument(
                DocumentInfo.Create(
                    DocumentId.CreateNewId(roslynProject.Id),
                    "NewClass.cs",
                    filePath: newFilePath,
                    loader: new NewClassTextLoader()));

            await Core.SolutionWriter.ApplyChangesAsync(sln, newDoc, false);

            Assert.True(File.Exists(newFilePath), $"Expected new file at {newFilePath}");
            var content = await File.ReadAllTextAsync(newFilePath);
            content.ShouldContain("class NewClass", Case.Sensitive);
        }
    }

    [Fact]
    public async Task does_not_write_added_documents()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
                                           namespace TestProject;
                                           public class Calculator
                                           {
                                               public int Add(int a, int b) => a + b;
                                           }
                                           """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var roslynProject = sln.Projects.First();
            var newFilePath = Path.Combine(project.ProjectDir, "NewClass.cs");
            var newDoc = sln.AddDocument(
                DocumentInfo.Create(
                    DocumentId.CreateNewId(roslynProject.Id),
                    "NewClass.cs",
                    filePath: newFilePath,
                    loader: new NewClassTextLoader()));

            // Capture output
            var originalOut = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                await Core.SolutionWriter.ApplyChangesAsync(sln, newDoc, true);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            Assert.False(File.Exists(newFilePath), "File should NOT be created in dry-run mode");
            var output = sw.ToString();
            output.ShouldContain("NewClass", Case.Sensitive);
        }
    }

    /// <summary>
    ///     Simple TextLoader that provides the content for the new document.
    /// </summary>
    private sealed class NewClassTextLoader : TextLoader
    {
        public override Task<TextAndVersion> LoadTextAndVersionAsync(
            LoadTextOptions options, CancellationToken cancellationToken)
        {
            var text = SourceText.From("""
                                       namespace TestProject;

                                       public class NewClass
                                       {
                                           public string Name => "New";
                                       }
                                       """);
            return Task.FromResult(TextAndVersion.Create(text, VersionStamp.Create()));
        }
    }
}
