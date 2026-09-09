using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class SearchSymbolsOperationTests
{
    [Fact]
    public async Task duplicate_simple_names_return_all_ranked_candidates_and_filters_disambiguate()
    {
        using var builder = new AdhocProjectBuilder("Orders");
        builder.AddSource("Result.cs", "namespace Orders; public sealed class Result { }");
        builder.AddProject("Payments")
            .AddSource("Result.cs", "namespace Payments; public sealed class Result { }");

        var envelope = await ExecuteAsync(builder, new SearchSymbolsRequest("Result"), ["Orders", "Payments"]);

        envelope.Outcome.ShouldBe("Succeeded");
        envelope.Data.Items.Count.ShouldBe(2);
        envelope.Data.Items.Select(item => item.Fqn).ShouldBe(["Orders.Result", "Payments.Result"]);
        envelope.Data.Items.ShouldAllBe(item => item.Kind == "NamedType");
        envelope.Data.Items.ShouldAllBe(item => item.Accessibility == "Public");
        envelope.Data.Items.ShouldAllBe(item => item.Rank == 2);
        envelope.Data.Items.Select(item => item.SymbolId).ShouldBeUnique();
        foreach (var item in envelope.Data.Items)
        {
            item.DeclaringPath.ShouldNotBeNull();
            Path.IsPathFullyQualified(item.DeclaringPath).ShouldBeTrue();
        }

        var filtered = await ExecuteAsync(
            builder,
            new SearchSymbolsRequest("Result", Project: "Payments", Namespace: "Payments"));

        filtered.Data.Items.ShouldHaveSingleItem().Project.ShouldBe("Payments");

        var exactFqn = await ExecuteAsync(
            builder,
            new SearchSymbolsRequest("Payments.Result", Kind: "NamedType"));
        exactFqn.Data.Items.ShouldHaveSingleItem().Rank.ShouldBe(1);
    }

    [Fact]
    public async Task exact_stable_identity_wins_without_fuzzy_selection()
    {
        using var builder = new AdhocProjectBuilder("IdentityApp");
        builder.AddSource("Customer.cs", "namespace IdentityApp; public sealed class Customer { }");
        var initial = await ExecuteAsync(builder, new SearchSymbolsRequest("Customer"));
        var symbolId = initial.Data.Items.ShouldHaveSingleItem().SymbolId;

        var resolved = await ExecuteAsync(builder, new SearchSymbolsRequest(symbolId));

        var candidate = resolved.Data.Items.ShouldHaveSingleItem();
        candidate.SymbolId.ShouldBe(symbolId);
        candidate.Rank.ShouldBe(0);
    }

    [Fact]
    public async Task kind_signature_generic_and_nested_filters_preserve_overloads()
    {
        using var builder = new AdhocProjectBuilder("GenericApp");
        builder.AddSource("Outer.cs", """
            namespace GenericApp;
            public class Outer<T>
            {
                public class Inner<TValue> { }
                public T Map(T value) => value;
                public string Map(string value) => value;
            }
            """);

        var nested = await ExecuteAsync(
            builder,
            new SearchSymbolsRequest(
                "Inner",
                Kind: "NamedType",
                Namespace: "GenericApp",
                GenericArity: 1));
        nested.Data.Items.ShouldHaveSingleItem().Fqn.ShouldContain("Outer<T>.Inner<TValue>");

        var overloads = await ExecuteAsync(
            builder,
            new SearchSymbolsRequest("Map", Kind: "Method", Signature: "Map("));
        overloads.Data.Items.Count.ShouldBe(2);
        overloads.Data.Items.Select(item => item.SymbolId).ShouldBeUnique();
        overloads.Data.Items.Select(item => item.Signature)
            .ShouldContain(signature => signature.Contains("string", StringComparison.Ordinal));
    }

    [Fact]
    public async Task fuzzy_matching_is_explicit_and_lower_ranked()
    {
        using var builder = new AdhocProjectBuilder("FuzzyApp");
        builder.AddSource("SearchSymbol.cs", "public sealed class SearchSymbol { }");

        var ordinary = await ExecuteAsync(builder, new SearchSymbolsRequest("SrchSym"));
        var fuzzy = await ExecuteAsync(builder, new SearchSymbolsRequest("SrchSym", Fuzzy: true));

        ordinary.Data.Items.ShouldBeEmpty();
        fuzzy.Data.Items.ShouldHaveSingleItem().Rank.ShouldBe(6);
    }

    [Fact]
    public async Task filtered_suffix_ranks_before_broad_partial_matches()
    {
        using var builder = new AdhocProjectBuilder("SuffixApp");
        builder.AddSource("OrderCustomer.cs", "public sealed class OrderCustomer { }");

        var envelope = await ExecuteAsync(
            builder,
            new SearchSymbolsRequest("Customer", Kind: "NamedType"));

        envelope.Data.Items.ShouldHaveSingleItem().Rank.ShouldBe(3);
    }

    [Fact]
    public async Task truncated_results_do_not_issue_an_unbound_continuation_token()
    {
        using var builder = new AdhocProjectBuilder("PagedApp");
        builder.AddSource("Things.cs", """
            public sealed class ThingA { }
            public sealed class ThingB { }
            public sealed class ThingC { }
            public sealed class ThingD { }
            """);

        var first = await ExecuteAsync(
            builder,
            new SearchSymbolsRequest("Thing", Kind: "NamedType", Limit: 2));

        first.Paging.TotalItems.ShouldBe(4);
        first.Paging.ReturnedItems.ShouldBe(2);
        first.Paging.Truncated.ShouldBeTrue();
        first.Paging.ContinuationToken.ShouldBeNull();
        first.Limitations.ShouldContain(limitation =>
            limitation.Contains("revision-safe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task stable_identity_from_an_older_source_revision_returns_typed_stale()
    {
        using var builder = new AdhocProjectBuilder("RevisionApp");
        builder.AddSource("Customer.cs", "namespace RevisionApp; public sealed class Customer { }");
        var (_, solution) = builder.Build();
        var service = new SymbolCandidateService();
        var initial = await service.SearchAsync(solution, new SymbolCandidateQuery("Customer"));
        var symbolId = initial.ShouldHaveSingleItem().Candidate.SymbolId;
        var document = solution.Projects.Single().Documents.Single();
        var changed = solution.WithDocumentText(
            document.Id,
            Microsoft.CodeAnalysis.Text.SourceText.From(
                "namespace RevisionApp; public sealed class Customer { public int Id { get; } }"));

        var resolved = await service.ResolveAsync(changed, symbolId, "type");

        var error = resolved.Match<RtError?>(_ => null, value => value);
        error.ShouldNotBeNull();
        error.Kind.ShouldBe(RtErrorKind.Stale);
    }

    [Fact]
    public async Task partial_workspace_is_visible_without_hiding_available_candidates()
    {
        using var builder = new AdhocProjectBuilder("PartialSearchApp");
        builder.AddSource(
            "Broken.cs",
            "namespace PartialSearchApp; public class Broken { MissingType Value = null!; }");

        var envelope = await ExecuteAsync(builder, new SearchSymbolsRequest("Broken"));

        envelope.Outcome.ShouldBe("Partial");
        envelope.Workspace.State.ShouldBe("Partial");
        envelope.Data.Items.ShouldHaveSingleItem().Name.ShouldBe("Broken");
        envelope.Confidence.Level.ShouldBe("Limited");
        envelope.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Message.Contains("CS0246", StringComparison.Ordinal));
    }

    private static async Task<RtResultEnvelope<SearchSymbolsResult>> ExecuteAsync(
        AdhocProjectBuilder builder,
        SearchSymbolsRequest request,
        IReadOnlyCollection<string>? expectedProjects = null) =>
        (await ExecuteResultAsync(builder, request, expectedProjects)).Unwrap();

    private static async Task<Result<RtResultEnvelope<SearchSymbolsResult>, RtError>> ExecuteResultAsync(
        AdhocProjectBuilder builder,
        SearchSymbolsRequest request,
        IReadOnlyCollection<string>? expectedProjects = null)
    {
        var (workspace, solution) = builder.Build();
        if (expectedProjects is not null)
            await ExplicitWorkspaceAcquisition.AttachAsync(solution, expectedProjects);
        var operation = new SearchSymbolsOperation(
            new FixedSessionFactory(workspace, solution),
            new SymbolCandidateService());
        return await operation.ExecuteAsync(request);
    }

    private sealed class FixedSessionFactory(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
