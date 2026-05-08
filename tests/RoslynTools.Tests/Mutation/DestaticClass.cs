using Microsoft.CodeAnalysis;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public sealed class DestaticClass
{
    [Fact]
    public async Task destatic_class_uses_constructor_injection_for_instance_consumers()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Helper.cs", """
                                       namespace TestApp;

                                       public static class Helper
                                       {
                                           public static string Format(string value) => value.Trim();
                                       }
                                       """);
        project.AddSource("Consumer.cs", """
                                         namespace TestApp;

                                         public class Consumer
                                         {
                                             public string Run(string value) => Helper.Format(value);
                                         }
                                         """);

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new DestaticClassService(
            new SymbolResolutionService(),
            new DocumentLocatorService(),
            writer).RunAsync(solution, "TestApp.Helper", false);

        var consumerText = await GetDocumentTextAsync(writer.Solution!, "Consumer.cs");

        Assert.Contains("private readonly Helper _helper;", consumerText);
        Assert.Contains("public Consumer(Helper helper)", consumerText);
        Assert.Contains("_helper = helper;", consumerText);
        Assert.Contains("_helper.Format(value)", consumerText);
        Assert.DoesNotContain("new Helper().Format(value)", consumerText);
    }

    [Fact]
    public async Task destatic_class_keeps_new_receiver_for_static_consumers()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Helper.cs", """
                                       namespace TestApp;

                                       public static class Helper
                                       {
                                           public static string Format(string value) => value.Trim();
                                       }
                                       """);
        project.AddSource("Consumer.cs", """
                                         namespace TestApp;

                                         public static class Consumer
                                         {
                                             public static string Run(string value) => Helper.Format(value);
                                         }
                                         """);

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new DestaticClassService(
            new SymbolResolutionService(),
            new DocumentLocatorService(),
            writer).RunAsync(solution, "TestApp.Helper", false);

        var consumerText = await GetDocumentTextAsync(writer.Solution!, "Consumer.cs");

        Assert.Contains("new Helper().Format(value)", consumerText);
        Assert.DoesNotContain("private readonly Helper _helper;", consumerText);
    }

    private static async Task<string> GetDocumentTextAsync(Solution solution, string fileName)
    {
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(document => Path.GetFileName(document.FilePath) == fileName);

        return (await document.GetTextAsync()).ToString();
    }

    private sealed class CapturingSolutionChangeWriter : ISolutionChangeWriter
    {
        public Solution? Solution { get; private set; }

        public Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun)
        {
            Solution = newSolution;
            return Task.CompletedTask;
        }
    }
}
