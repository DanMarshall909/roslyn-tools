using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public sealed class SolidOperationTests
{
    [Theory]
    [InlineData("SRP")]
    [InlineData("ISP")]
    [InlineData("DIP")]
    [InlineData("OCP")]
    [InlineData("LSP")]
    public async Task requested_principle_runs_only_its_detector(string principle)
    {
        var result = await ExecuteAsync(principle);

        result.SRP.Count.ShouldBe(principle == "SRP" ? 1 : 0);
        result.ISP.FatInterfaces.Count.ShouldBe(principle == "ISP" ? 1 : 0);
        result.DIP.ConcreteConstructorParams.Count.ShouldBe(principle == "DIP" ? 2 : 0);
        result.OCP.Count.ShouldBe(principle == "OCP" ? 1 : 0);
        result.LSP.ThrowingOverrides.Count.ShouldBe(principle == "LSP" ? 1 : 0);
    }

    [Fact]
    public async Task all_runs_every_detector()
    {
        var result = await ExecuteAsync("all");

        result.SRP.ShouldHaveSingleItem().TypeName.ShouldBe("SrpCandidate");
        result.ISP.FatInterfaces.ShouldHaveSingleItem().InterfaceName.ShouldBe("IFat");
        result.DIP.ConcreteConstructorParams.Count.ShouldBe(2);
        result.OCP.ShouldHaveSingleItem().TypeName.ShouldBe("OcpCandidate");
        result.LSP.ThrowingOverrides.ShouldHaveSingleItem().TypeName.ShouldBe("LspCandidate");
    }

    private static async Task<SolidResult> ExecuteAsync(string principle)
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("SolidCandidates.cs", """
            public sealed class SrpDependency { }

            public sealed class SrpCandidate
            {
                public SrpCandidate(SrpDependency dependency) { }
                public void Run() { }
            }

            public interface IFat
            {
                void First();
                void Second();
            }

            public sealed class ConcreteDependency { }

            public sealed class DipCandidate
            {
                public DipCandidate(ConcreteDependency dependency) { }
            }

            public abstract class OcpBase { }
            public sealed class OcpOne : OcpBase { }
            public sealed class OcpTwo : OcpBase { }

            public sealed class OcpCandidate
            {
                public string Map(OcpBase value) => value switch
                {
                    OcpOne => "one",
                    OcpTwo => "two",
                    _ => "other"
                };
            }

            public abstract class LspBase
            {
                public abstract void Save();
            }

            public sealed class LspCandidate : LspBase
            {
                public override void Save() => throw new System.NotSupportedException();
            }
            """);
        var (workspace, solution) = project.Build();
        var operation = new SolidOperation(new FixedWorkspaceSessionFactory(workspace, solution));

        var execution = await operation.ExecuteAsync(new SolidRequest(
            Principle: principle,
            MinMethods: 1,
            MinDeps: 1,
            MinMembers: 2,
            MinBranches: 2));

        return execution.Match(
            value => value,
            error => throw new ShouldAssertException(error.Message));
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
