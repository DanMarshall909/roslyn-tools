using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class AddTest : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public AddTest(RtServicesFixture services) => _services = services;

    // ──────────────────────────────────────────────────────────────────────────
    //  1. AcToMethodName pure conversion — no Roslyn needed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void creates_valid_test_identifiers()
    {
        _services.Resolve<AddTestService>().AcToMethodName("Returns 404 when order not found").ShouldBe("Returns_404_When_Order_Not_Found");
    }

    [Fact]
    public void strips_punctuation()
    {
        _services.Resolve<AddTestService>().AcToMethodName("It works!").ShouldBe("It_Works");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Single mode: appends [Fact] stub to existing test class
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task appends_a_fact_method()
    {
        using var proj = new AdhocProjectBuilder();
        // Source has no usings — command must add them
        proj.AddSource("MyTests.cs", """
                                     namespace TestApp;

                                     public class MyTests
                                     {
                                     }
                                     """);

        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, proj.Directory))
        {
            await ExecuteLegacyAsync(
                "MyTests",
                "Should_DoSomething",
                null,
                null,
                null,
                "ignored",
                false);
        }

        var content = await proj.ReadSourceAsync("MyTests.cs");

        content.ShouldContain("[Fact]", Case.Sensitive);
        content.ShouldContain("public void Should_DoSomething()", Case.Sensitive);
        content.ShouldContain("throw new NotImplementedException();", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. AC mode: converts ACs to method names and adds stubs
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task generates_test_methods()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("OrderTests.cs", """
                                        namespace TestApp;

                                        public class OrderTests
                                        {
                                        }
                                        """);

        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, proj.Directory))
        {
            await ExecuteLegacyAsync(
                "OrderTests",
                null,
                "Returns 404 when not found; Creates order successfully",
                null,
                null,
                "ignored",
                false);
        }

        var content = await proj.ReadSourceAsync("OrderTests.cs");

        content.ShouldContain("public void Returns_404_When_Not_Found()", Case.Sensitive);
        content.ShouldContain("public void Creates_Order_Successfully()", Case.Sensitive);
        content.ShouldContain("throw new NotImplementedException();", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. AC mode with --nested-class: wraps methods in nested class
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task wraps_generated_methods_in_the_nested_class()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("OrderTests.cs", """
                                        namespace TestApp;

                                        public class OrderTests
                                        {
                                        }
                                        """);

        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, proj.Directory))
        {
            await ExecuteLegacyAsync(
                "OrderTests",
                null,
                "Returns 404 when not found",
                "WhenOrderMissing",
                null,
                "ignored",
                false);
        }

        var content = await proj.ReadSourceAsync("OrderTests.cs");

        content.ShouldContain("public class WhenOrderMissing", Case.Sensitive);
        content.ShouldContain("public void Returns_404_When_Not_Found()", Case.Sensitive);

        // Nested class must appear inside outer class
        var outerIdx = content.IndexOf("public class OrderTests", StringComparison.Ordinal);
        var nestedIdx = content.IndexOf("public class WhenOrderMissing", StringComparison.Ordinal);
        Assert.True(outerIdx < nestedIdx, "Nested class must appear inside outer class");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Dry-run: does not write files
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_modify_the_file()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("MyTests.cs", """
                                     namespace TestApp;

                                     public class MyTests
                                     {
                                     }
                                     """);

        var (workspace, sln) = proj.Build();
        var originalContent = await proj.ReadSourceAsync("MyTests.cs");

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, proj.Directory))
        {
            var execution = await ExecuteLegacyAsync(
                "MyTests",
                "Should_DoSomething",
                null,
                null,
                null,
                "ignored",
                true);

            execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
            execution.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("MyTests.cs");
        }

        var afterContent = await proj.ReadSourceAsync("MyTests.cs");
        afterContent.ShouldBe(originalContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  6. Adds using directives when missing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task adds_missing_using_directives()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("MyTests.cs", """
                                     namespace TestApp;

                                     public class MyTests
                                     {
                                     }
                                     """);

        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, proj.Directory))
        {
            await ExecuteLegacyAsync(
                "MyTests",
                "Should_DoSomething",
                null,
                null,
                null,
                "ignored",
                false);
        }

        var content = await proj.ReadSourceAsync("MyTests.cs");

        content.ShouldContain("using Xunit;", Case.Sensitive);
        content.ShouldContain("using System;", Case.Sensitive);
        content.ShouldContain("public void Should_DoSomething()", Case.Sensitive);
    }

    private async Task<MutationExecutionResult<AddTestResult>> ExecuteLegacyAsync(
        string className,
        string? name,
        string? acceptanceCriteria,
        string? nestedClass,
        string? filePath,
        string? solution,
        bool dryRun)
    {
        var result = await _services.Resolve<AddTestOperation>().ExecuteAsync(new AddTestRequest(
            AddTestMode.Legacy,
            ClassName: className,
            Name: name,
            AcceptanceCriteria: acceptanceCriteria,
            NestedClass: nestedClass,
            FilePath: filePath,
            Solution: solution,
            DryRun: dryRun));

        return result.Unwrap();
    }
}
