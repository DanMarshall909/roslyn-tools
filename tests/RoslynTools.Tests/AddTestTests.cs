using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests;

public class AddTestTests
{
    // ──────────────────────────────────────────────────────────────────────────
    //  1. AcToMethodName pure conversion — no Roslyn needed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AcToMethodName_ConvertsAcStringToIdentifier()
    {
        Assert.Equal("Returns_404_When_Order_Not_Found",
            AddTestCommand.AcToMethodName("Returns 404 when order not found"));
    }

    [Fact]
    public void AcToMethodName_StripsPunctuation()
    {
        Assert.Equal("It_Works",
            AddTestCommand.AcToMethodName("It works!"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Single mode: appends [Fact] stub to existing test class
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleMode_AppendsSingleFactMethod()
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddTestCommand.ExecuteAsync(
            className: "MyTests",
            name: "Should_DoSomething",
            acs: null,
            nestedClass: null,
            filePath: null,
            solutionPath: "ignored",
            dryRun: false);

        var content = await proj.ReadSourceAsync("MyTests.cs");

        Assert.Contains("[Fact]", content);
        Assert.Contains("public void Should_DoSomething()", content);
        Assert.Contains("throw new NotImplementedException();", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. AC mode: converts ACs to method names and adds stubs
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcMode_GeneratesMethodsFromAcs()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("OrderTests.cs", """
            namespace TestApp;

            public class OrderTests
            {
            }
            """);

        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddTestCommand.ExecuteAsync(
                className: "OrderTests",
                name: null,
                acs: "Returns 404 when not found; Creates order successfully",
                nestedClass: null,
                filePath: null,
                solutionPath: "ignored",
                dryRun: false);

        var content = await proj.ReadSourceAsync("OrderTests.cs");

        Assert.Contains("public void Returns_404_When_Not_Found()", content);
        Assert.Contains("public void Creates_Order_Successfully()", content);
        Assert.Contains("throw new NotImplementedException();", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. AC mode with --nested-class: wraps methods in nested class
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcMode_WithNestedClass_WrapsInNestedClass()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("OrderTests.cs", """
            namespace TestApp;

            public class OrderTests
            {
            }
            """);

        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddTestCommand.ExecuteAsync(
            className: "OrderTests",
            name: null,
            acs: "Returns 404 when not found",
            nestedClass: "WhenOrderMissing",
            filePath: null,
            solutionPath: "ignored",
            dryRun: false);

        var content = await proj.ReadSourceAsync("OrderTests.cs");

        Assert.Contains("public class WhenOrderMissing", content);
        Assert.Contains("public void Returns_404_When_Not_Found()", content);

        // Nested class must appear inside outer class
        var outerIdx = content.IndexOf("public class OrderTests", StringComparison.Ordinal);
        var nestedIdx = content.IndexOf("public class WhenOrderMissing", StringComparison.Ordinal);
        Assert.True(outerIdx < nestedIdx, "Nested class must appear inside outer class");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Dry-run: does not write files
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DryRun_DoesNotModifyFile()
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddTestCommand.ExecuteAsync(
            className: "MyTests",
            name: "Should_DoSomething",
            acs: null,
            nestedClass: null,
            filePath: null,
            solutionPath: "ignored",
            dryRun: true);

        var afterContent = await proj.ReadSourceAsync("MyTests.cs");
        Assert.Equal(originalContent, afterContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  6. Adds using directives when missing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleMode_AddsUsingDirectivesWhenMissing()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("MyTests.cs", """
            namespace TestApp;

            public class MyTests
            {
            }
            """);

        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddTestCommand.ExecuteAsync(
            className: "MyTests",
            name: "Should_DoSomething",
            acs: null,
            nestedClass: null,
            filePath: null,
            solutionPath: "ignored",
            dryRun: false);

        var content = await proj.ReadSourceAsync("MyTests.cs");

        Assert.Contains("using Xunit;", content);
        Assert.Contains("using System;", content);
        Assert.Contains("public void Should_DoSomething()", content);
    }
}
