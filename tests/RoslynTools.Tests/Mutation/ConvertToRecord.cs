using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ConvertToRecord
{
    private const string RecordAppSource = """
                                           class Point { public int X { get; init; } public int Y { get; init; } }
                                           class WithEquals {
                                             public int Value { get; init; }
                                             public override bool Equals(object? obj) => obj is WithEquals o && o.Value == Value;
                                             public override int GetHashCode() => Value.GetHashCode();
                                           }
                                           class WithMutableProp { public int Id { get; set; } public string Name { get; init; } = ""; }
                                           """;

    // ──────────────────────────────────────────────────────────────────────────
    //  1. class Point becomes record Point
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task converts_the_class_to_a_record()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using var trust = RepositoryTrustContext.UseExplicitTrust(proj.Directory);
        (await ExecuteAsync(workspace, sln, "Point")).IsOk.ShouldBeTrue();

        var content = await proj.ReadSourceAsync("Types.cs");
        content.ShouldContain("record Point", Case.Sensitive);
        content.ShouldNotContain("class Point", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. init-only properties become positional parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task converts_init_only_properties_to_primary_parameters()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using var trust = RepositoryTrustContext.UseExplicitTrust(proj.Directory);
        (await ExecuteAsync(workspace, sln, "Point")).IsOk.ShouldBeTrue();

        var content = await proj.ReadSourceAsync("Types.cs");
        // Positional record syntax: record Point(int X, int Y)
        content.ShouldContain("int X", Case.Sensitive);
        content.ShouldContain("int Y", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Explicit Equals/GetHashCode overrides are removed (record provides them)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task removes_explicit_equality_members()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using var trust = RepositoryTrustContext.UseExplicitTrust(proj.Directory);
        (await ExecuteAsync(workspace, sln, "WithEquals")).IsOk.ShouldBeTrue();

        var content = await proj.ReadSourceAsync("Types.cs");
        content.ShouldNotContain("override bool Equals", Case.Sensitive);
        content.ShouldNotContain("override int GetHashCode", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Mutable properties: still converts, warns on stderr about mutability
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task warns_but_still_converts_the_class()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using var trust = RepositoryTrustContext.UseExplicitTrust(proj.Directory);
        var execution = (await ExecuteAsync(workspace, sln, "WithMutableProp")).Unwrap();

        execution.Diagnostics.ShouldHaveSingleItem().Message
            .ShouldContain("mutable", Case.Insensitive);

        var content = await proj.ReadSourceAsync("Types.cs");
        content.ShouldContain("record WithMutableProp", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. --dry-run shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_modify_files()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Types.cs");

        (await ExecuteAsync(workspace, sln, "Point", dryRun: true)).IsOk.ShouldBeTrue();

        var after = await proj.ReadSourceAsync("Types.cs");
        after.ShouldBe(before);
    }

    private static Task<Result<MutationExecutionResult<ConvertToRecordResult>, RtError>> ExecuteAsync(
        Workspace workspace,
        Solution solution,
        string type,
        bool dryRun = false)
    {
        var operation = new ConvertToRecordOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new ConvertToRecordService(
                new SymbolResolutionService(),
                new DocumentLocatorService()),
            new SolutionChangeWriter());
        return operation.ExecuteAsync(new ConvertToRecordRequest(type, DryRun: dryRun));
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
