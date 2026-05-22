using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class DependencyInjection
{
    [Fact]
    public async Task update_only_the_fully_qualified_target_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         namespace App.Sales
                                         {
                                             public interface IEmailService { }

                                             public class AgentService
                                             {
                                             }
                                         }

                                         namespace App.Support
                                         {
                                             public class AgentService
                                             {
                                             }
                                         }
                                         """);

        var (_, solution) = project.Build();
        var result = await new AddDependencyService(
                NullLogger<AddDependencyService>.Instance,
                new CapturingSolutionChangeWriter())
            .ApplyAsync(solution, "App.Sales.AgentService", "IEmailService", null);

        var text = await TestSolutionDocuments.GetTextAsync(result.Solution, "Services.cs");

        result.Changed.ShouldBeTrue();
        result.TargetType.ShouldBe("App.Sales.AgentService");
        text.ShouldContain("public AgentService(IEmailService emailService)", Case.Sensitive);
        text.ShouldContain("private readonly IEmailService _emailService;", Case.Sensitive);
        text.ShouldContain("namespace App.Support", Case.Sensitive);
    }

    [Fact]
    public async Task reject_an_ambiguous_short_type_name()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         namespace App.Sales
                                         {
                                             public class AgentService { }
                                         }

                                         namespace App.Support
                                         {
                                             public class AgentService { }
                                         }
                                         """);

        var (_, solution) = project.Build();
        var originalExitCode = Environment.ExitCode;

        try
        {
            Environment.ExitCode = 0;

            var result = await new AddDependencyService(
                    NullLogger<AddDependencyService>.Instance,
                    new CapturingSolutionChangeWriter())
                .ApplyAsync(solution, "AgentService", "IEmailService", null);

            result.Changed.ShouldBeFalse();
            result.Solution.ShouldBeSameAs(solution);
            result.TargetType.ShouldBeNull();
            Environment.ExitCode.ShouldBe(1);
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
        }
    }

    [Fact]
    public async Task leave_existing_dependency_wiring_unchanged()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
                                             namespace App;

                                             public interface IEmailService { }

                                             public class AgentService
                                             {
                                                 private readonly IEmailService _emailService;

                                                 public AgentService(IEmailService emailService)
                                                 {
                                                     _emailService = emailService;
                                                 }
                                             }
                                             """);

        var (_, solution) = project.Build();
        var result = await new AddDependencyService(
                NullLogger<AddDependencyService>.Instance,
                new CapturingSolutionChangeWriter())
            .ApplyAsync(solution, "AgentService", "IEmailService", null);

        result.Changed.ShouldBeFalse();
        result.Solution.ShouldBeSameAs(solution);
        result.TargetType.ShouldBe("App.AgentService");
    }

    [Fact]
    public async Task reject_expression_bodied_constructors_that_need_assignment()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
                                             namespace App;

                                             public interface IEmailService { }

                                             public class AgentService
                                             {
                                                 private readonly string _name;

                                                 public AgentService(string name) => _name = name;
                                             }
                                             """);

        var (_, solution) = project.Build();
        var originalExitCode = Environment.ExitCode;

        try
        {
            Environment.ExitCode = 0;

            var result = await new AddDependencyService(
                    NullLogger<AddDependencyService>.Instance,
                    new CapturingSolutionChangeWriter())
                .ApplyAsync(solution, "AgentService", "IEmailService", null);

            result.Changed.ShouldBeFalse();
            result.Solution.ShouldBeSameAs(solution);
            result.TargetType.ShouldBe("App.AgentService");
            Environment.ExitCode.ShouldBe(1);
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
        }
    }

    [Theory]
    [InlineData("global::App.AgentService", "App.AgentService")]
    [InlineData(" App.AgentService ", "App.AgentService")]
    public void normalize_type_names_for_symbol_matching(string typeName, string normalizedTypeName)
    {
        new AddDependencyService(
            NullLogger<AddDependencyService>.Instance,
            new CapturingSolutionChangeWriter()).NormalizeTypeName(typeName).ShouldBe(normalizedTypeName);
    }

    [Theory]
    [InlineData("IEmailService", "_emailService")]
    [InlineData("Logger", "_logger")]
    public void derive_field_names_from_dependency_types(string dependencyType, string fieldName)
    {
        new AddDependencyService(
            NullLogger<AddDependencyService>.Instance,
            new CapturingSolutionChangeWriter()).DeriveFieldName(dependencyType).ShouldBe(fieldName);
    }
}
