using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

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

        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        (await ExecuteAsync(workspace, solution, writer)).IsOk.ShouldBeTrue();

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");

        consumerText.ShouldContain("private readonly Helper _helper;", Case.Sensitive);
        consumerText.ShouldContain("public Consumer(Helper helper)", Case.Sensitive);
        consumerText.ShouldContain("_helper = helper;", Case.Sensitive);
        consumerText.ShouldContain("_helper.Format(value)", Case.Sensitive);
        consumerText.ShouldNotContain("new Helper().Format(value)", Case.Sensitive);
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

        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        (await ExecuteAsync(workspace, solution, writer)).IsOk.ShouldBeTrue();

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");

        consumerText.ShouldContain("new Helper().Format(value)", Case.Sensitive);
        consumerText.ShouldNotContain("private readonly Helper _helper;", Case.Sensitive);
    }

    [Fact]
    public async Task removes_static_modifiers_from_the_migrated_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Helper.cs", """
                                       namespace TestApp;

                                       public static class Helper
                                       {
                                           public static string Prefix = ">";
                                           public static string Name { get; set; } = "helper";
                                           public static string Format(string value) => Prefix + value;
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        var execution = (await ExecuteAsync(workspace, solution, writer)).Unwrap();

        var helperText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Helper.cs");

        helperText.ShouldContain("using System.Diagnostics.CodeAnalysis;", Case.Sensitive);
        helperText.ShouldContain("[SuppressMessage", Case.Sensitive);
        helperText.ShouldContain("public class Helper", Case.Sensitive);
        helperText.ShouldContain("public string Prefix", Case.Sensitive);
        helperText.ShouldContain("public string Name", Case.Sensitive);
        helperText.ShouldContain("public string Format(string value)", Case.Sensitive);
        helperText.ShouldNotContain("public static class Helper", Case.Sensitive);
        writer.DryRun.ShouldBe(false);
        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
    }

    [Fact]
    public async Task reject_types_that_are_not_static_classes()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Helper.cs", """
                                       namespace TestApp;

                                       public class Helper
                                       {
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        var result = await ExecuteAsync(workspace, solution, writer);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => string.Empty, error => error.Message)
            .ShouldContain("is not a static class", Case.Sensitive);
        writer.Solution.ShouldBeNull();
    }

    [Fact]
    public async Task add_dependencies_to_existing_instance_constructors()
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
                                             private readonly string _name;

                                             public Consumer(string name)
                                             {
                                                 _name = name;
                                             }

                                             public string Run(string value) => Helper.Format(value);
                                         }
                                         """);

        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        (await ExecuteAsync(workspace, solution, writer)).IsOk.ShouldBeTrue();

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");

        consumerText.ShouldContain("private readonly Helper _helper;", Case.Sensitive);
        consumerText.ShouldContain("public Consumer(", Case.Sensitive);
        Assert.True(consumerText.Contains("Helper helper", StringComparison.Ordinal), consumerText);
        consumerText.ShouldContain("_helper = helper;", Case.Sensitive);
        consumerText.ShouldContain("_name = name;", Case.Sensitive);
    }

    [Fact]
    public async Task injected_field_and_synthesised_constructor_appear_on_separate_lines()
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

        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        (await ExecuteAsync(workspace, solution, writer)).IsOk.ShouldBeTrue();

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");
        var lines = consumerText.Split('\n');
        var fieldLineIndex = Array.FindIndex(lines, l => l.Contains("private readonly Helper _helper;"));
        var ctorLineIndex = Array.FindIndex(lines, l => l.Contains("public Consumer(Helper helper)"));

        Assert.True(fieldLineIndex >= 0, "Field declaration not found");
        Assert.True(ctorLineIndex >= 0, "Constructor not found");
        ctorLineIndex.ShouldNotBe(fieldLineIndex);
    }

    [Fact]
    public async Task use_new_receivers_for_field_initializers()
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
                                             private readonly string _value = Helper.Format("x");
                                         }
                                         """);

        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        (await ExecuteAsync(workspace, solution, writer)).IsOk.ShouldBeTrue();

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");

        consumerText.ShouldContain("private readonly string _value = new Helper().Format(\"x\");", Case.Sensitive);
        consumerText.ShouldNotContain("private readonly Helper _helper;", Case.Sensitive);
    }

    private static Task<Result<MutationExecutionResult<DestaticClassResult>, RtError>> ExecuteAsync(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer)
    {
        var operation = new DestaticClassOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new DestaticClassService(
                new SymbolResolutionService(),
                new DocumentLocatorService()),
            writer);
        return operation.ExecuteAsync(new DestaticClassRequest("TestApp.Helper", DryRun: false));
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
