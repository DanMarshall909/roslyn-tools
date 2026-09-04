using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

/// <summary>
/// find-unused costs roughly <c>candidate symbols × one Roslyn reference search</c>, and a reference
/// search is the expensive half. These tests pin the left-hand side: how many candidates a body of
/// source produces and how many searches those candidates cost. Timings belong in a benchmark, not
/// in a suite — a duration assertion measures the machine — but a query count is exact, so it is the
/// thing worth holding still. It is also what makes an optimisation checkable: a run that got faster
/// without asking fewer questions has not been optimised, it has been lucky.
/// </summary>
public class FindUnusedQueryCost
{
    [Fact]
    public async Task a_partial_type_is_one_candidate_however_many_files_declare_it()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.Fields.cs", "public partial class Widget { private int _count; }");
        project.AddSource("Widget.Methods.cs", "public partial class Widget { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var profiler = new FindUnusedProfiler();

            var results = await new FindUnusedService().AnalyzeAsync(solution, "types", profiler);

            var profile = profiler.Snapshot();
            profile.TypeCandidates.ShouldBe(2, "both declarations are met while walking documents");
            profile.UniqueCandidates.ShouldBe(1, "they declare one type between them");
            profile.ReferenceQueries.ShouldBe(1,
                "asking Roslyn the same question once per declaration is the cost this analysis is "
                + "made of, and the answer cannot differ between two halves of one type");
            results.Count(r => r.Name == "Widget").ShouldBe(1,
                "one type is one finding — reporting it per declaration would offer the caller the "
                + "same deletion twice");
        }
    }

    [Fact]
    public async Task a_partial_method_is_one_candidate_across_its_definition_and_implementation()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Host.cs", """
                                     public partial class Host
                                     {
                                         public partial void Run();
                                     }
                                     """);
        project.AddSource("Host.Implementation.cs", """
                                                    public partial class Host
                                                    {
                                                        public partial void Run() { }
                                                    }
                                                    """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var profiler = new FindUnusedProfiler();

            var results = await new FindUnusedService().AnalyzeAsync(solution, "methods", profiler);

            var profile = profiler.Snapshot();
            profile.MethodCandidates.ShouldBe(2, "a partial method is declared twice");
            profile.UniqueCandidates.ShouldBe(1,
                "the definition and the implementation are separate symbols describing one method, "
                + "so canonicalising to the definition is what collapses them");
            profile.ReferenceQueries.ShouldBe(1);
            results.Count(r => r.Name == "Run").ShouldBe(1);
        }
    }

    [Fact]
    public async Task the_analysis_can_be_cancelled_once_it_has_started()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", "public class Orphan { public void Unused() { } }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(
                () => new FindUnusedService().AnalyzeAsync(
                    solution, "all", profiler: null, cancellation.Token),
                "the workspace is loaded before the analysis begins, so a token checked only either "
                + "side of the load leaves the expensive part uninterruptible and the command "
                + "indistinguishable from hung");
        }
    }
}
