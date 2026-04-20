using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

public class SolutionWriterIntegrationTests
{
    [Fact]
    public async Task Writes_added_documents_to_new_files()
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

            await SolutionWriter.ApplyChangesAsync(sln, newDoc, dryRun: false);

            Assert.True(File.Exists(newFilePath), $"Expected new file at {newFilePath}");
            var content = await File.ReadAllTextAsync(newFilePath);
            Assert.Contains("class NewClass", content);
        }
    }

    [Fact]
    public async Task Does_not_write_added_documents()
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
                await SolutionWriter.ApplyChangesAsync(sln, newDoc, dryRun: true);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            Assert.False(File.Exists(newFilePath), "File should NOT be created in dry-run mode");
            var output = sw.ToString();
            Assert.Contains("NewClass", output);
        }
    }

    /// <summary>
    /// Simple TextLoader that provides the content for the new document.
    /// </summary>
    private sealed class NewClassTextLoader : TextLoader
    {
        public override Task<TextAndVersion> LoadTextAndVersionAsync(
            LoadTextOptions options, CancellationToken cancellationToken)
        {
            var text = Microsoft.CodeAnalysis.Text.SourceText.From("""
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
