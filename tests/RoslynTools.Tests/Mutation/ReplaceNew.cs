using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public sealed class ConstructorReplacement
{
    [Fact]
    public async Task rewrites_constructor_calls_to_factory_calls()
    {
        const string source = """
                              namespace Sample;

                              public class Widget
                              {
                                  public Widget(string name) { }
                              }

                              public static class WidgetFactory
                              {
                                  public static Widget Create(string name) => null!;
                              }

                              public class Consumer
                              {
                                  public Widget Build() => new Widget("primary");
                              }
                              """;

        using var project = new AdhocProjectBuilder()
            .AddSource("Consumer.cs", source);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var writer = new CapturingSolutionChangeWriter();
            var service = new ReplaceNewService(NullLogger<ReplaceNewService>.Instance, writer);

            await service.RunAsync(solution, "Widget", "WidgetFactory.Create", false);

            var updatedText = await TestSolutionDocuments.GetTextAsync(writer.NewSolution!, "Consumer.cs");
            Assert.Contains("public Widget Build() => WidgetFactory.Create(\"primary\");", updatedText);
            Assert.Contains("public static Widget Create(string name) => null!;", updatedText);
            Assert.False(writer.DryRun);
        }
    }

    [Fact]
    public async Task replaces_only_constructor_calls_matching_the_literal_argument_filter()
    {
        const string source = """
                              namespace Sample;

                              public class Widget
                              {
                                  public Widget(string name) { }
                              }

                              public static class WidgetFactory
                              {
                                  public static Widget Create(string name) => null!;
                              }

                              public class Consumer
                              {
                                  public Widget Primary() => new Widget("primary");
                                  public Widget Secondary() => new Widget("secondary");
                              }
                              """;

        using var project = new AdhocProjectBuilder()
            .AddSource("Consumer.cs", source);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var writer = new CapturingSolutionChangeWriter();
            var service = new ReplaceNewService(NullLogger<ReplaceNewService>.Instance, writer);

            await service.RunAsync(
                solution,
                "Widget",
                "WidgetFactory.Create",
                false,
                0,
                "primary");

            var updatedText = await TestSolutionDocuments.GetTextAsync(writer.NewSolution!, "Consumer.cs");
            Assert.Contains("public Widget Primary() => WidgetFactory.Create(\"primary\");", updatedText);
            Assert.Contains("public Widget Secondary() => new Widget(\"secondary\");", updatedText);
        }
    }

    [Fact]
    public async Task leaves_the_solution_unchanged_when_no_constructor_calls_match()
    {
        const string source = """
                              namespace Sample;

                              public class Consumer
                              {
                                  public string Build() => "nothing";
                              }
                              """;

        using var project = new AdhocProjectBuilder()
            .AddSource("Consumer.cs", source);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var writer = new CapturingSolutionChangeWriter();
            var service = new ReplaceNewService(NullLogger<ReplaceNewService>.Instance, writer);

            await service.RunAsync(solution, "Widget", "WidgetFactory.Create", false);

            Assert.Null(writer.NewSolution);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task leaves_constructor_calls_when_the_argument_filter_index_is_out_of_range(int argumentIndex)
    {
        const string source = """
                              namespace Sample;

                              public class Widget
                              {
                                  public Widget(string name) { }
                              }

                              public class Consumer
                              {
                                  public Widget Build() => new Widget("primary");
                              }
                              """;

        using var project = new AdhocProjectBuilder()
            .AddSource("Consumer.cs", source);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var writer = new CapturingSolutionChangeWriter();
            var service = new ReplaceNewService(NullLogger<ReplaceNewService>.Instance, writer);

            await service.RunAsync(
                solution,
                "Widget",
                "WidgetFactory.Create",
                false,
                argumentIndex,
                "primary");

            Assert.Null(writer.NewSolution);
        }
    }
}
