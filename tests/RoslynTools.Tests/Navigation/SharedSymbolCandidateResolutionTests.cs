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

    private sealed class FixedSessionFactory(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
