using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

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

        var (_, solution) = project.Build();
        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new NamespaceToNamespaceMove("Sample.Legacy", "Sample.Domain", MoveFiles: false))).Unwrap();

        var widgetText = await TestSolutionDocuments.GetTextAsync(preparation.Solution, "Widget.cs");
        var consumerText = await TestSolutionDocuments.GetTextAsync(preparation.Solution, "Consumer.cs");

        widgetText.ShouldContain("namespace Sample.Domain;", Case.Sensitive);
        consumerText.ShouldContain("using Sample.Domain;", Case.Sensitive);
        consumerText.ShouldContain("public Sample.Domain.Widget Build() => new Sample.Domain.Widget();", Case.Sensitive);
        preparation.ToChangeSet(solution).HasChanges.ShouldBeTrue();
    }

    [Fact]
    public async Task report_not_found_without_changing_the_solution_when_no_namespace_matches()
    {
        using var project = new AdhocProjectBuilder("Sample");
        project.AddSource("Widget.cs", """
                                       namespace Sample.Current;

                                       public class Widget
                                       {
                                       }
                                       """);

        var (_, solution) = project.Build();
        var result = await new MoveService().PrepareAsync(
            solution,
            new NamespaceToNamespaceMove("Sample.Missing", "Sample.Domain", MoveFiles: false));

        var error = result.Match(
            _ => throw new ShouldAssertException("Expected a not-found result."),
            failure => failure);
        error.Kind.ShouldBe(RtErrorKind.NotFound);
        error.Message.ShouldContain("Sample.Missing", Case.Sensitive);
        (await project.ReadSourceAsync("Widget.cs"))
            .ShouldContain("namespace Sample.Current;", Case.Sensitive);
    }
}
