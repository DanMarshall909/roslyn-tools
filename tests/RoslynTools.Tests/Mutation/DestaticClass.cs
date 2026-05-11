using Microsoft.Extensions.Logging.Abstractions;
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
            NullLogger<DestaticClassService>.Instance,
            new SymbolResolutionService(),
            new DocumentLocatorService(),
            writer).RunAsync(solution, "TestApp.Helper", false);

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");

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
            NullLogger<DestaticClassService>.Instance,
            new SymbolResolutionService(),
            new DocumentLocatorService(),
            writer).RunAsync(solution, "TestApp.Helper", false);

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");

        Assert.Contains("new Helper().Format(value)", consumerText);
        Assert.DoesNotContain("private readonly Helper _helper;", consumerText);
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

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new DestaticClassService(
            NullLogger<DestaticClassService>.Instance,
            new SymbolResolutionService(),
            new DocumentLocatorService(),
            writer).RunAsync(solution, "TestApp.Helper", false);

        var helperText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Helper.cs");

        Assert.Contains("using System.Diagnostics.CodeAnalysis;", helperText);
        Assert.Contains("[SuppressMessage", helperText);
        Assert.Contains("public class Helper", helperText);
        Assert.Contains("public string Prefix", helperText);
        Assert.Contains("public string Name", helperText);
        Assert.Contains("public string Format(string value)", helperText);
        Assert.DoesNotContain("public static class Helper", helperText);
        Assert.False(writer.DryRun);
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

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DestaticClassService(
                NullLogger<DestaticClassService>.Instance,
                new SymbolResolutionService(),
                new DocumentLocatorService(),
                writer).RunAsync(solution, "TestApp.Helper", false));

        Assert.Contains("is not a static class", exception.Message);
        Assert.Null(writer.Solution);
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

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new DestaticClassService(
            NullLogger<DestaticClassService>.Instance,
            new SymbolResolutionService(),
            new DocumentLocatorService(),
            writer).RunAsync(solution, "TestApp.Helper", false);

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");

        Assert.Contains("private readonly Helper _helper;", consumerText);
        Assert.Contains("public Consumer(", consumerText);
        Assert.True(consumerText.Contains("Helper helper", StringComparison.Ordinal), consumerText);
        Assert.Contains("_helper = helper;", consumerText);
        Assert.Contains("_name = name;", consumerText);
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

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new DestaticClassService(
            NullLogger<DestaticClassService>.Instance,
            new SymbolResolutionService(),
            new DocumentLocatorService(),
            writer).RunAsync(solution, "TestApp.Helper", false);

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");
        var lines = consumerText.Split('\n');
        var fieldLineIndex = Array.FindIndex(lines, l => l.Contains("private readonly Helper _helper;"));
        var ctorLineIndex = Array.FindIndex(lines, l => l.Contains("public Consumer(Helper helper)"));

        Assert.True(fieldLineIndex >= 0, "Field declaration not found");
        Assert.True(ctorLineIndex >= 0, "Constructor not found");
        Assert.NotEqual(fieldLineIndex, ctorLineIndex);
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

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new DestaticClassService(
            NullLogger<DestaticClassService>.Instance,
            new SymbolResolutionService(),
            new DocumentLocatorService(),
            writer).RunAsync(solution, "TestApp.Helper", false);

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Consumer.cs");

        Assert.Contains("private readonly string _value = new Helper().Format(\"x\");", consumerText);
        Assert.DoesNotContain("private readonly Helper _helper;", consumerText);
    }
}
