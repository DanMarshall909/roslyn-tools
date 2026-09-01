using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class RelationshipsOperationTests
{
    [Fact]
    public async Task references_return_bounded_common_items()
    {
        using var builder = CreateRelationshipProject();
        var (workspace, solution) = builder.Build();
        var operation = CreateOperation(workspace, solution);

        var envelope = (await operation.ExecuteAsync(new RelationshipsRequest(
            "App.IWorker",
            RelationshipMode.References,
            Kind: "type"))).Unwrap();

        envelope.SchemaVersion.ShouldBe("rt.result.v1");
        envelope.Operation.ShouldBe("relationships");
        envelope.Data.Mode.ShouldBe(RelationshipMode.References);
        envelope.Data.ResolvedSubject.ShouldBe("App.IWorker");
        envelope.Data.Items.ShouldNotBeEmpty();
        envelope.Data.Items.ShouldAllBe(item => item.Relation == "Reference");
        envelope.Source.Revision.ShouldNotBeNullOrWhiteSpace();
        envelope.Limitations.ShouldContain(limitation =>
            limitation.Contains("reflection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task callers_accept_stable_method_identity_from_shared_search()
    {
        using var builder = CreateRelationshipProject();
        var (workspace, solution) = builder.Build();
        var symbols = new SymbolCandidateService();
        var matches = await symbols.SearchAsync(
            solution,
            new SymbolCandidateQuery("App.IWorker.Work()", Kind: "Method"));
        var identity = matches.ShouldHaveSingleItem().Candidate.SymbolId;
        var operation = CreateOperation(workspace, solution, symbols);

        var envelope = (await operation.ExecuteAsync(new RelationshipsRequest(
            identity,
            RelationshipMode.Callers,
            IncludeTests: true))).Unwrap();

        var caller = envelope.Data.Items.ShouldHaveSingleItem();
        caller.Relation.ShouldBe("Caller");
        caller.TargetSymbol.ShouldBe("Run");
        caller.File.ShouldEndWith("Relationships.cs");
    }

    [Fact]
    public async Task implementations_preserve_symbol_and_project_evidence()
    {
        using var builder = CreateRelationshipProject();
        var (workspace, solution) = builder.Build();
        var operation = CreateOperation(workspace, solution);

        var envelope = (await operation.ExecuteAsync(new RelationshipsRequest(
            "App.IWorker",
            RelationshipMode.Implementations))).Unwrap();

        var implementation = envelope.Data.Items.ShouldHaveSingleItem();
        implementation.Relation.ShouldBe("Implementation");
        implementation.TargetSymbol.ShouldBe("App.Worker");
        implementation.TargetKind.ShouldBe("Class");
        implementation.Project.ShouldBe("RelationshipApp");
        implementation.Evidence.ShouldNotBeNull();
        implementation.Evidence.ShouldContain("abstract=False");
    }

    [Fact]
    public async Task references_propagate_cancellation_after_symbol_resolution()
    {
        using var builder = CreateRelationshipProject();
        var (workspace, solution) = builder.Build();
        using var cancellation = new CancellationTokenSource();
        var symbols = new CancelAfterResolveSymbolService(cancellation);
        var operation = CreateOperation(workspace, solution, symbols);

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new RelationshipsRequest("App.IWorker", RelationshipMode.References, Kind: "type"),
            cancellation.Token));
    }

    private static AdhocProjectBuilder CreateRelationshipProject() =>
        new AdhocProjectBuilder("RelationshipApp")
            .AddSource("Relationships.cs", """
                namespace App;
                public interface IWorker { void Work(); }
                public sealed class Worker : IWorker { public void Work() { } }
                public sealed class Consumer { public void Run(IWorker worker) => worker.Work(); }
                """);

    private static RelationshipsOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        ISymbolCandidateService? symbols = null)
    {
        symbols ??= new SymbolCandidateService();
        return new RelationshipsOperation(
            new FixedSessionFactory(workspace, solution),
            new FindRefsService(new RtConsoleLogger<FindRefsService>(), symbols),
            new FindCallersService(new RtConsoleLogger<FindCallersService>(), symbols),
            new FindImplementationsService(),
            symbols);
    }

    private sealed class CancelAfterResolveSymbolService(CancellationTokenSource cancellation)
        : ISymbolCandidateService
    {
        public Task<IReadOnlyList<ResolvedSymbolCandidate>> SearchAsync(
            Solution solution,
            SymbolCandidateQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResolvedSymbolCandidate>>([]);

        public async Task<Result<ISymbol, RtError>> ResolveAsync(
            Solution solution,
            string identityOrName,
            string? kind,
            CancellationToken cancellationToken = default)
        {
            var compilation = await solution.Projects.Single().GetCompilationAsync(cancellationToken);
            var symbol = compilation!.GetTypeByMetadataName("App.IWorker")!;
            cancellation.Cancel();
            return Result<ISymbol, RtError>.Ok(symbol);
        }

        public Task<Result<ISymbol, RtError>> ResolveAsync(
            Solution solution,
            string identityOrName,
            string? kind,
            string sourceRevision,
            CancellationToken cancellationToken = default) =>
            ResolveAsync(solution, identityOrName, kind, cancellationToken);
    }

    private sealed class FixedSessionFactory(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
