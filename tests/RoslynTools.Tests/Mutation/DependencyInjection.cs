using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Services;

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

        Assert.True(result.Changed);
        Assert.Equal("App.Sales.AgentService", result.TargetType);
        Assert.Contains("public AgentService(IEmailService emailService)", text);
        Assert.Contains("private readonly IEmailService _emailService;", text);
        Assert.Contains("namespace App.Support", text);
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

            Assert.False(result.Changed);
            Assert.Same(solution, result.Solution);
            Assert.Null(result.TargetType);
            Assert.Equal(1, Environment.ExitCode);
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

        Assert.False(result.Changed);
        Assert.Same(solution, result.Solution);
        Assert.Equal("App.AgentService", result.TargetType);
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

            Assert.False(result.Changed);
            Assert.Same(solution, result.Solution);
            Assert.Equal("App.AgentService", result.TargetType);
            Assert.Equal(1, Environment.ExitCode);
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
        Assert.Equal(normalizedTypeName, new AddDependencyService(
            NullLogger<AddDependencyService>.Instance,
            new CapturingSolutionChangeWriter()).NormalizeTypeName(typeName));
    }

    [Theory]
    [InlineData("IEmailService", "_emailService")]
    [InlineData("Logger", "_logger")]
    public void derive_field_names_from_dependency_types(string dependencyType, string fieldName)
    {
        Assert.Equal(fieldName, new AddDependencyService(
            NullLogger<AddDependencyService>.Instance,
            new CapturingSolutionChangeWriter()).DeriveFieldName(dependencyType));
    }

}
