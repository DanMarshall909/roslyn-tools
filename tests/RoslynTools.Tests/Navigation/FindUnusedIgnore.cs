using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

/// <summary>
/// find-unused consults .rtignore before analysing a document. Coverage showed that guard
/// had never once executed, meaning the feature was entirely unverified for this command —
/// an ignored file could have been reported as dead code and nobody would have known.
/// </summary>
public class FindUnusedIgnore
{
    [Fact]
    public async Task does_not_report_types_in_an_rtignored_file()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Generated.cs", "public class GeneratedOrphan { }");
        project.AddSource("Models.cs", "public class RealOrphan { }");

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            // The ad-hoc workspace sets no solution FilePath, so RtIgnore infers its root from
            // the project file's directory. A pattern with no '/' or '*' matches a path segment:
            // "Generated" matches "/Generated." and so covers Generated.cs.
            await File.WriteAllTextAsync(
                Path.Combine(project.ProjectDir, ".rtignore"),
                "Generated\n");

            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");
            var names = results.Select(result => result.Name).ToList();

            names.ShouldNotContain("GeneratedOrphan",
                "an .rtignore'd file is out of scope, so its types must not be reported as dead");
            names.ShouldContain("RealOrphan",
                "ignoring one file must not silence the rest of the solution");
        }
    }
}
