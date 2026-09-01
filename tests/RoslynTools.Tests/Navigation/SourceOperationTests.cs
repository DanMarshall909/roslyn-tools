using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class SourceOperationTests
{
    [Fact]
    public async Task ExecuteAsync_loads_requested_solution_and_returns_symbol_content()
    {
        using var project = new AdhocProjectBuilder("SourceApp");
        project.AddSource("Calculator.cs", """
            namespace App;
            public class Calculator
            {
                /// <summary>Adds two values.</summary>
                public int Add(int left, int right) => left + right;
            }
            """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var factory = new CapturingSessionFactory(solution);
            var operation = new SourceOperation(factory, new InspectService(), new SymbolCandidateService());

            var result = await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add", "requested.slnx", "method"));
            var source = result.Unwrap();

            operation.Definition.Name.ShouldBe("source");
            factory.RequestedSolution.ShouldBe("requested.slnx");
            source.Subject.ShouldBe("App.Calculator.Add(int, int)");
            var entry = source.Declarations.ShouldHaveSingleItem();
            entry.FullyQualifiedName.ShouldBe("App.Calculator.Add(int, int)");
            entry.Text.ReplaceLineEndings("\n").ShouldBe("""
                /// <summary>Adds two values.</summary>
                public int Add(int left, int right) => left + right;
                """.ReplaceLineEndings("\n"));
            source.RelatedSymbols.ShouldBeEmpty();
            source.RelatedSymbolsTruncated.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task ExecuteAsync_lists_related_symbols_without_including_their_content_by_default()
    {
        using var project = CreateRelatedSourceProject();
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new SourceOperation(
                new CapturingSessionFactory(solution),
                new InspectService(),
                new SymbolCandidateService());

            var source = (await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add", Kind: "method"))).Unwrap();

            var related = source.RelatedSymbols.ShouldHaveSingleItem();
            related.FullyQualifiedName.ShouldBe("App.IFormatter.Format(int)");
            related.Kind.ShouldBe("Method");
            related.Declarations.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ExecuteAsync_includes_one_hop_related_symbol_content_when_requested()
    {
        using var project = CreateRelatedSourceProject();
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new SourceOperation(
                new CapturingSessionFactory(solution),
                new InspectService(),
                new SymbolCandidateService());

            var source = (await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add",
                Kind: "method",
                IncludeRelated: true))).Unwrap();

            var declaration = source.RelatedSymbols.ShouldHaveSingleItem()
                .Declarations.ShouldNotBeNull()
                .ShouldHaveSingleItem();
            declaration.Text.ShouldBe("string Format(int value);");
        }
    }

    [Fact]
    public async Task ExecuteAsync_bounds_related_symbols_and_reports_truncation()
    {
        using var project = new AdhocProjectBuilder("SourceApp");
        project.AddSource("Calculator.cs", """
            namespace App;
            public static class First { public static int Get() => 1; }
            public static class Second { public static int Get() => 2; }
            public sealed class Calculator
            {
                public int Add() => First.Get() + Second.Get();
            }
            """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new SourceOperation(
                new CapturingSessionFactory(solution),
                new InspectService(),
                new SymbolCandidateService());

            var source = (await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add",
                Kind: "method",
                RelatedLimit: 1))).Unwrap();

            source.RelatedSymbols.ShouldHaveSingleItem()
                .FullyQualifiedName.ShouldBe("App.First.Get()");
            source.RelatedSymbolsTruncated.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task ExecuteAsync_pages_utf8_source_without_splitting_scalars()
    {
        const string sourceText = """
            namespace App;
            public static class Texts
            {
                public static string Read() => "😀😀😀😀😀😀";
            }
            """;
        using var project = new AdhocProjectBuilder("SourceApp")
            .AddSource("Source.cs", sourceText);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new SourceOperation(
                new CapturingSessionFactory(solution),
                new InspectService(),
                new SymbolCandidateService());
            var expected = (await operation.ExecuteAsync(
                    new SourceRequest("App.Texts.Read", Kind: "method")))
                .Unwrap().Declarations.ShouldHaveSingleItem().Text;

            const int sourceByteBudget = 17;
            var chunks = new List<string>();
            string? continuation = null;
            var expectedStart = 0;
            SourceResult? firstPage = null;
            for (var pageNumber = 0; pageNumber < 20; pageNumber++)
            {
                var page = (await operation.ExecuteAsync(new SourceRequest(
                        "App.Texts.Read",
                        Kind: "method",
                        MaxSourceBytes: sourceByteBudget,
                        ContinuationToken: continuation)))
                    .Unwrap();
                firstPage ??= page;

                page.SourceRevision.ShouldNotBeNullOrWhiteSpace();
                page.SourceBytesReturned.ShouldBeLessThanOrEqualTo(sourceByteBudget);
                var declaration = page.Declarations.ShouldHaveSingleItem();
                var chunkBytes = Encoding.UTF8.GetByteCount(declaration.Text);
                chunkBytes.ShouldBe(page.SourceBytesReturned);
                declaration.ReturnedFromByte.ShouldBe(expectedStart);
                declaration.ReturnedToByteExclusive.ShouldBe(expectedStart + chunkBytes);
                declaration.Text.ShouldNotContain("�", Case.Sensitive);
                chunks.Add(declaration.Text);
                expectedStart = declaration.ReturnedToByteExclusive;
                continuation = page.ContinuationToken;
                if (continuation is null)
                {
                    page.Truncated.ShouldBeFalse();
                    break;
                }

                page.Truncated.ShouldBeTrue();
            }

            firstPage.ShouldNotBeNull();
            firstPage.SourceBytesTotal.ShouldBe(Encoding.UTF8.GetByteCount(expected));
            ((long)firstPage.SourceBytesReturned).ShouldBeLessThan(firstPage.SourceBytesTotal);
            continuation.ShouldBeNull("the continuation must eventually consume the declaration");
            string.Concat(chunks).ShouldBe(expected);
        }
    }

    [Fact]
    public async Task ExecuteAsync_rejects_continuation_after_the_source_revision_changes()
    {
        const string original = """
            namespace App;
            public static class Texts
            {
                public static string Read() => "original source that needs another page";
            }
            """;
        using var project = new AdhocProjectBuilder("SourceApp")
            .AddSource("Source.cs", original);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var first = (await new SourceOperation(
                    new CapturingSessionFactory(solution),
                    new InspectService(),
                    new SymbolCandidateService())
                .ExecuteAsync(new SourceRequest(
                    "App.Texts.Read",
                    Kind: "method",
                    MaxSourceBytes: 16))).Unwrap();

            var documentId = solution.Projects.Single().DocumentIds.Single();
            var changed = solution.WithDocumentText(
                documentId,
                SourceText.From(original.Replace("original", "changed", StringComparison.Ordinal)));
            var continued = await new SourceOperation(
                    new CapturingSessionFactory(changed),
                    new InspectService(),
                    new SymbolCandidateService())
                .ExecuteAsync(new SourceRequest(
                    "App.Texts.Read",
                    Kind: "method",
                    MaxSourceBytes: 16,
                    ContinuationToken: first.ContinuationToken));

            continued.IsErr.ShouldBeTrue();
            continued.Match(_ => default!, error => error).Kind.ShouldBe(RtErrorKind.Stale);
        }
    }

    [Fact]
    public async Task ExecuteAsync_rejects_continuation_with_different_related_selection()
    {
        const string sourceText = """
            namespace App;
            public static class Texts
            {
                public static string Read() => "source that needs another page";
            }
            """;
        using var project = new AdhocProjectBuilder("SourceApp")
            .AddSource("Source.cs", sourceText);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new SourceOperation(
                new CapturingSessionFactory(solution),
                new InspectService(),
                new SymbolCandidateService());
            var first = (await operation.ExecuteAsync(new SourceRequest(
                "App.Texts.Read",
                Kind: "method",
                MaxSourceBytes: 16))).Unwrap();

            var continued = await operation.ExecuteAsync(new SourceRequest(
                "App.Texts.Read",
                Kind: "method",
                RelatedLimit: 9,
                MaxSourceBytes: 16,
                ContinuationToken: first.ContinuationToken));

            continued.IsErr.ShouldBeTrue();
            continued.Match(_ => default!, error => error).Kind.ShouldBe(RtErrorKind.InvalidInput);
        }
    }

    [Fact]
    public async Task ExecuteAsync_budgets_subject_declarations_before_related_content()
    {
        using var project = CreateRelatedSourceProject();
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new SourceOperation(
                new CapturingSessionFactory(solution),
                new InspectService(),
                new SymbolCandidateService());

            var page = (await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add",
                Kind: "method",
                IncludeRelated: true,
                MaxSourceBytes: 16))).Unwrap();

            page.Declarations.ShouldHaveSingleItem().Text.ShouldNotBeEmpty();
            page.RelatedSymbols.ShouldHaveSingleItem()
                .Declarations.ShouldNotBeNull().ShouldBeEmpty();
            page.SourceBytesReturned.ShouldBeLessThanOrEqualTo(16);
            page.Truncated.ShouldBeTrue();
            page.ContinuationToken.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(1_048_577)]
    public async Task ExecuteAsync_rejects_source_byte_budgets_outside_the_declared_range(
        int maximumSourceBytes)
    {
        const string sourceText = "namespace App; public static class Texts { public static int Read() => 1; }";
        using var project = new AdhocProjectBuilder("SourceApp")
            .AddSource("Source.cs", sourceText);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var result = await new SourceOperation(
                    new CapturingSessionFactory(solution),
                    new InspectService(),
                    new SymbolCandidateService())
                .ExecuteAsync(new SourceRequest(
                    "App.Texts.Read",
                    Kind: "method",
                    MaxSourceBytes: maximumSourceBytes));

            result.IsErr.ShouldBeTrue();
            result.Match(_ => default!, error => error).Kind.ShouldBe(RtErrorKind.InvalidInput);
        }
    }

    [Fact]
    public void cli_exposes_the_same_source_budget_and_continuation_controls()
    {
        var command = App.Commands.Single(candidate => candidate.Info.Name == "source").Build();

        command.Options.Select(option => option.Name).ShouldContain("max-source-bytes");
        command.Options.Select(option => option.Name).ShouldContain("continuation");
    }

    [Fact]
    public void constructor_requires_symbol_candidate_service()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SourceOperation(
                new CapturingSessionFactory(null!),
                new InspectService(),
                null!));
    }

    [Fact]
    public async Task ExecuteAsync_honors_cancellation_before_loading_solution()
    {
        using var project = new AdhocProjectBuilder("SourceApp");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var factory = new CapturingSessionFactory(solution);
            var operation = new SourceOperation(factory, new InspectService(), new SymbolCandidateService());

            await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
                new SourceRequest("App.Missing", "requested.slnx"),
                new CancellationToken(canceled: true)));

            factory.RequestedSolution.ShouldBeNull();
        }
    }

    private static AdhocProjectBuilder CreateRelatedSourceProject() =>
        new AdhocProjectBuilder("SourceApp")
            .AddSource("Calculator.cs", """
                namespace App;
                public interface IFormatter
                {
                    string Format(int value);
                }

                public sealed class Calculator(IFormatter formatter)
                {
                    public string Add(int left, int right) => formatter.Format(left + right);
                }
                """);

    private sealed class CapturingSessionFactory(Solution solution) : IWorkspaceSessionFactory
    {
        public string? RequestedSolution { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath)
        {
            RequestedSolution = solutionPath;
            return Task.FromResult(solution);
        }

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
        {
            RequestedSolution = solutionPath;
            return Task.FromResult((solution.Workspace, solution));
        }
    }
}
