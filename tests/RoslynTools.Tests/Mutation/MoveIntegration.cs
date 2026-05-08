using RoslynTools.Services;

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

        var (workspace, solution) = project.Build();

        await new MoveService().ExecuteSingleTypeMoveAsync(
            workspace,
            solution,
            "Foo.Target",
            "Bar",
            false,
            false);

        var movedSource = await project.ReadSourceAsync("Target.cs");

        Assert.Contains("namespace Bar;", movedSource);
        Assert.Contains("using Foo;", movedSource);
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

        var (workspace, solution) = project.Build();

        await new MoveService().ExecuteSingleTypeMoveAsync(
            workspace,
            solution,
            "Foo.Target",
            "Bar",
            false,
            false);

        var consumerAfter = await project.ReadSourceAsync("Consumer.cs");

        Assert.Contains("using Bar;", consumerAfter);
    }

    [Fact]
    public async Task handles_single_type_move_with_move_file_relocates_source_file_to_match_target_namespace()
    {
        const string source = """
                              namespace SampleProject.Foo;
                              public class Movable
                              {
                                  public void Run() { }
                              }
                              """;

        using var project = new AdhocProjectBuilder("SampleProject")
            .AddSource("Foo/Movable.cs", source);

        var (workspace, solution) = project.Build();

        await new MoveService().ExecuteSingleTypeMoveAsync(
            workspace,
            solution,
            "SampleProject.Foo.Movable",
            "SampleProject.Bar",
            true,
            false);

        Assert.False(project.SourceExists("Foo/Movable.cs"), "old folder should not contain the file");
        Assert.True(project.SourceExists("Bar/Movable.cs"), "file should be relocated to new folder");
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

        var (workspace, solution) = project.Build();

        await new MoveService().ExecuteSingleTypeMoveAsync(
            workspace,
            solution,
            "Foo.Target",
            "Bar",
            false,
            false);

        var consumerAfter = await project.ReadSourceAsync("Consumer.cs");

        Assert.Contains("using Bar;", consumerAfter);
    }
}
