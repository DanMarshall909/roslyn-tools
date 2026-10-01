using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class MoveIntegration
{
    [Fact]
    public async Task handles_single_type_move_preserves_sibling_references_via_old_namespace_using()
    {
        const string helperSource = """
                                    namespace Foo;
                                    public static class Helper
                                    {
                                        public static void Ping() { }
                                    }
                                    """;

        const string targetSource = """
                                    namespace Foo;
                                    public class Target
                                    {
                                        public void Run() => Helper.Ping();
                                    }
                                    """;

        using var project = new AdhocProjectBuilder("MoveSample")
            .AddSource("Helper.cs", helperSource)
            .AddSource("Target.cs", targetSource);

        var (_, solution) = project.Build();
        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new TypeToNamespaceMove("Foo.Target", "Bar", MoveFile: false))).Unwrap();
        await project.ApplySolutionAsync(solution, preparation.Solution);

        var movedSource = await project.ReadSourceAsync("Target.cs");

        movedSource.ShouldContain("namespace Bar;", Case.Sensitive);
        movedSource.ShouldContain("using Foo;", Case.Sensitive);
    }

    [Fact]
    public async Task handles_single_type_move_adds_new_namespace_using_to_unqualified_consumers()
    {
        const string targetSource = """
                                    namespace Foo;
                                    public static class Target
                                    {
                                        public static void Run() { }
                                    }
                                    """;

        const string consumerSource = """
                                      using Foo;
                                      namespace App;
                                      public class Consumer
                                      {
                                          public void Use() => Target.Run();
                                      }
                                      """;

        using var project = new AdhocProjectBuilder("MoveSample")
            .AddSource("Target.cs", targetSource)
            .AddSource("Consumer.cs", consumerSource);

        var (_, solution) = project.Build();
        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new TypeToNamespaceMove("Foo.Target", "Bar", MoveFile: false))).Unwrap();
        await project.ApplySolutionAsync(solution, preparation.Solution);

        var consumerAfter = await project.ReadSourceAsync("Consumer.cs");

        consumerAfter.ShouldContain("using Bar;", Case.Sensitive);
    }

    [Fact]
    public async Task handles_single_type_move_updates_consumers_of_co_located_types_in_the_moved_file()
    {
        const string movedFileSource = """
                                       namespace Foo;
                                       public class Target { }
                                       public record CompanionDto(string Name);
                                       """;

        const string consumerSource = """
                                      using Foo;
                                      namespace App;
                                      public class Consumer
                                      {
                                          public CompanionDto Build() => new("x");
                                      }
                                      """;

        using var project = new AdhocProjectBuilder("CoLocatedSample")
            .AddSource("MovedFile.cs", movedFileSource)
            .AddSource("Consumer.cs", consumerSource);

        var (_, solution) = project.Build();
        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new TypeToNamespaceMove("Foo.Target", "Bar", MoveFile: false))).Unwrap();
        await project.ApplySolutionAsync(solution, preparation.Solution);

        var consumerAfter = await project.ReadSourceAsync("Consumer.cs");

        consumerAfter.ShouldContain("using Bar;", Case.Sensitive);
    }

    [Fact]
    public async Task handles_single_type_move_rewrites_fully_qualified_consumers()
    {
        const string targetSource = """
                                    namespace Foo;
                                    public class Target
                                    {
                                    }
                                    """;

        const string consumerSource = """
                                      namespace App;
                                      public class Consumer
                                      {
                                          public Foo.Target Build() => new Foo.Target();
                                      }
                                      """;

        using var project = new AdhocProjectBuilder("MoveSample")
            .AddSource("Target.cs", targetSource)
            .AddSource("Consumer.cs", consumerSource);

        var (_, solution) = project.Build();
        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new TypeToNamespaceMove("Foo.Target", "Bar", MoveFile: false))).Unwrap();
        await project.ApplySolutionAsync(solution, preparation.Solution);

        var consumerAfter = await project.ReadSourceAsync("Consumer.cs");

        consumerAfter.ShouldContain("public Bar.Target Build() => new Bar.Target();", Case.Sensitive);
    }
}
