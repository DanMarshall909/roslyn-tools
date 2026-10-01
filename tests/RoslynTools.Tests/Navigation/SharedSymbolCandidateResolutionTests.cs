using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class SharedSymbolCandidateResolutionTests
{
    [Fact]
    public async Task referenced_project_symbols_are_not_duplicated_as_consumer_declarations()
    {
        using var builder = new AdhocProjectBuilder("CoreProject");
        builder.AddSource("Service.cs", "namespace Shared; public sealed class Service { }");
        builder.AddProject("ConsumerProject")
            .ReferenceProject("CoreProject")
            .AddSource("Use.cs", "namespace Consumer; public sealed class Use(Shared.Service service) { }");
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var matches = await new SymbolCandidateService().SearchAsync(
                solution,
                new SymbolCandidateQuery("Shared.Service", Kind: "NamedType"));

            matches.ShouldHaveSingleItem().Candidate.Project.ShouldBe("CoreProject");
        }
    }

    [Fact]
    public async Task duplicate_fqn_is_ambiguous_but_stable_identity_drives_describe_and_source()
    {
        using var builder = new AdhocProjectBuilder("Production");
        builder.AddSource("Result.cs", "namespace Shared; public sealed class Result { }");
        builder.AddProject("ConsumerTests")
            .AddSource("Result.cs", "namespace Shared; public sealed class Result { public string Fixture => \"test\"; }");
        var (workspace, solution) = builder.Build();
        var candidates = new SymbolCandidateService();

        var ambiguous = await candidates.ResolveAsync(solution, "Shared.Result", "type");
        var ambiguity = ambiguous.Match<RtError?>(_ => null, error => error);
        ambiguity.ShouldNotBeNull();
        ambiguity.Kind.ShouldBe(RtErrorKind.AmbiguousMatch);
        ambiguity.Context!["candidates"].ShouldContain("Production");
        ambiguity.Context["candidates"].ShouldContain("ConsumerTests");
        ambiguity.Context["candidateCount"].ShouldBe("2");
        ambiguity.Context["displayedCandidateCount"].ShouldBe("2");
        ambiguity.Context["candidatesTruncated"].ShouldBe("false");

        var matches = await candidates.SearchAsync(
            solution,
            new SymbolCandidateQuery("Shared.Result", Kind: "NamedType"));
        var testIdentity = matches.Single(match => match.Candidate.Project == "ConsumerTests")
            .Candidate.SymbolId;

        var inspection = await new InspectService(candidates: candidates)
            .Describe(solution, testIdentity, "type");
        var compact = inspection.Unwrap().ShouldBeOfType<CompactDescription>();
        compact.Fqn.ShouldBe("Shared.Result");
        compact.Signatures.ShouldContain(signature => signature.Contains("Fixture", StringComparison.Ordinal));

        var source = await new SourceOperation(
                new FixedSessionFactory(workspace, solution),
                new InspectService(candidates: candidates),
                candidates)
            .ExecuteAsync(new SourceRequest(testIdentity, Kind: "type"));
        var declaration = source.Unwrap().Declarations.ShouldHaveSingleItem();
        declaration.FilePath.ShouldContain("ConsumerTests");
        declaration.Text.ShouldContain("Fixture");
    }

    [Fact]
    public async Task many_ambiguous_symbols_return_bounded_candidates_and_a_full_search_route()
    {
        using var builder = new AdhocProjectBuilder("Production");
        builder.AddSource("Workers.cs", string.Join(Environment.NewLine,
            Enumerable.Range(0, 80).Select(index =>
                $"namespace N{index:D3} {{ public sealed class Worker {{ public void ExecuteAsync() {{ }} }} }}")));
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var resolver = new SymbolCandidateService();
            var result = await resolver.ResolveAsync(solution, "ExecuteAsync", "method");
            var error = result.Match<RtError?>(_ => null, failure => failure);

            error.ShouldNotBeNull();
            error.Kind.ShouldBe(RtErrorKind.AmbiguousMatch);
            error.Context!["candidateCount"].ShouldBe("80");
            var displayed = int.Parse(error.Context["displayedCandidateCount"],
                CultureInfo.InvariantCulture);
            displayed.ShouldBeInRange(1, 12);
            error.Context["candidatesTruncated"].ShouldBe("true");
            var listed = error.Context["candidates"].Split('\n');
            listed.Length.ShouldBe(displayed);
            error.Context["candidates"].ShouldContain("N000.Worker.ExecuteAsync()");
            error.Context["candidates"].ShouldNotContain("N079.Worker.ExecuteAsync()");
            error.Context["disambiguateWith"].ShouldContain("search-symbols");
            error.Message.ShouldContain("search-symbols");
            error.Message.ShouldContain("with filters");
            error.Message.ShouldNotContain("full candidate set");
            Encoding.UTF8.GetByteCount(error.Message).ShouldBeLessThanOrEqualTo(2_048);
            Encoding.UTF8.GetByteCount(error.Context["candidates"])
                .ShouldBeLessThanOrEqualTo(4_096);

            var firstIdentity = listed[0].Split(" | ", StringSplitOptions.None)[0];
            var selected = await resolver.ResolveAsync(solution, firstIdentity, "method");
            selected.Unwrap().ToDisplayString().ShouldContain("N000.Worker.ExecuteAsync()");
        }
    }

    [Fact]
    public async Task unusually_long_ambiguous_names_still_produce_a_bounded_error()
    {
        var name = "Find" + new string('Ω', 800);
        using var builder = new AdhocProjectBuilder("Production");
        builder.AddSource("Workers.cs",
            $"namespace First {{ public sealed class Worker {{ public void {name}() {{ }} }} }} "
            + $"namespace Second {{ public sealed class Worker {{ public void {name}() {{ }} }} }}");
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var result = await new SymbolCandidateService()
                .ResolveAsync(solution, name, "method");
            var error = result.Match<RtError?>(_ => null, failure => failure);

            error.ShouldNotBeNull();
            error.Kind.ShouldBe(RtErrorKind.AmbiguousMatch);
            error.Context!["candidateCount"].ShouldBe("2");
            error.Context["displayedCandidateCount"].ShouldBe("0");
            error.Context["candidatesTruncated"].ShouldBe("true");
            error.Context["symbolTruncated"].ShouldBe("true");
            error.Context["symbol"].ShouldNotContain('�');
            error.Message.ShouldContain("search-symbols");
            Encoding.UTF8.GetByteCount(error.Message).ShouldBeLessThanOrEqualTo(2_048);
            Encoding.UTF8.GetByteCount(error.Context["candidates"])
                .ShouldBeLessThanOrEqualTo(4_096);
        }
    }

    private sealed class FixedSessionFactory(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
