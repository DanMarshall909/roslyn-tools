using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public sealed class NamespaceMoves
{
    [Fact]
    public async Task rewrite_matching_declarations_and_namespace_references()
    {
        using var project = new AdhocProjectBuilder("Sample");
        project.AddSource("Domain/Widget.cs", """
                                              namespace Sample.Legacy;

                                              public class Widget
                                              {
                                              }
                                              """);
        project.AddSource("Consumer.cs", """
                                         using Sample.Legacy;

                                         namespace Sample.App;

                                         public class Consumer
                                         {
                                             public Sample.Legacy.Widget Build() => new Sample.Legacy.Widget();
                                         }
                                         """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var writer = new CapturingSolutionChangeWriter();
            var service = new MoveService(NullLogger<MoveService>.Instance, writer);

            await service.ExecuteNamespaceMoveAsync(
                workspace,
                solution,
                "Sample.Legacy",
                "Sample.Domain",
                false,
                false);

            var widgetText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Widget.cs");
            var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");

            Assert.Contains("namespace Sample.Domain;", widgetText);
            Assert.Contains("using Sample.Domain;", consumerText);
            Assert.Contains("public Sample.Domain.Widget Build() => new Sample.Domain.Widget();", consumerText);
            Assert.False(writer.DryRun);
        }
    }

    [Fact]
    public async Task leave_the_solution_unchanged_when_no_namespace_matches()
    {
        using var project = new AdhocProjectBuilder("Sample");
        project.AddSource("Widget.cs", """
                                       namespace Sample.Current;

                                       public class Widget
                                       {
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var writer = new CapturingSolutionChangeWriter();
            var service = new MoveService(NullLogger<MoveService>.Instance, writer);

            await service.ExecuteNamespaceMoveAsync(
                workspace,
                solution,
                "Sample.Missing",
                "Sample.Domain",
                false,
                false);

            Assert.Null(writer.Solution);
        }
    }
}
