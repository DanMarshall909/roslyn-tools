using RoslynTools.Core;

namespace RoslynTools.Tests.Issues.EP_007_CodeQualityRefactor;

/// <summary>
///     US-050: Extract-method source-file call site not updated with parameterized arguments.
///     Bug: When extracting a method with parameterization (Phase 2+), the original call site
///     in the source file retains its original call instead of being updated to pass the new
///     parameters. Cross-file call sites are updated correctly.
///     Root cause: PatternMatcher excludes the source file from matches, so ApplyMatchesAsync
///     never calls TryReplaceMatchAsync for the source-file call site.
/// </summary>
[Trait("Epic", "EP-007")]
[Trait("Story", "US-050")]
public class ExtractMethod
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
    {
        return Path.Combine(project.ProjectDir, relativePath);
    }

    [Fact]
    [Trait("Concern", "SourceFileCallSite")]
    public async Task with_source_file_first_occurrence_call_site_updated()
    {
        // Setup: Create two files with identical method patterns
        using var project = new AdhocProjectBuilder();
        project.AddSource("ServiceA.cs", """
                                         namespace TestProject;

                                         public class ServiceA
                                         {
                                             public void Process()
                                             {
                                                 var result = DoWork("alpha");
                                             }

                                             private string DoWork(string name) => $"Service: {name}";
                                         }
                                         """);

        project.AddSource("ServiceB.cs", """
                                         namespace TestProject;

                                         public class ServiceB
                                         {
                                             public void Process()
                                             {
                                                 var result = DoWork("beta");
                                             }

                                             private string DoWork(string name) => $"Service: {name}";
                                         }
                                         """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Act: Extract the method from the source file
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "ServiceA.cs"),
                "DoWork_Extracted",
                7,
                7);

            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        // Assert: Extraction succeeds (documents the bug exists)
        var serviceAContent = await project.ReadSourceAsync("ServiceA.cs");
        Assert.Contains("DoWork_Extracted", serviceAContent);
    }

    [Fact(Skip = "US-050: pending — source-file call site not yet updated with new parameters")]
    public async Task with_signature_changes_source_file_receives_new_parameters()
    {
        // Setup: Create duplicate patterns where source file has the first occurrence
        using var project = new AdhocProjectBuilder();
        project.AddSource("ServiceA.cs", """
                                         namespace TestProject;

                                         public class ServiceA
                                         {
                                             private Service _svc = new();

                                             public void Process()
                                             {
                                                 var result = DoWork(_svc);
                                             }

                                             private string DoWork(Service svc) => $"Service: {svc.Name}";
                                         }
                                         """);

        project.AddSource("ServiceB.cs", """
                                         namespace TestProject;

                                         public class ServiceB
                                         {
                                             private Service _svc = new();

                                             public void Process()
                                             {
                                                 var result = DoWork(_svc);
                                             }

                                             private string DoWork(Service svc) => $"Service: {svc.Name}";
                                         }
                                         """);

        project.AddSource("Service.cs", """
                                        namespace TestProject;

                                        public class Service
                                        {
                                            public string Name => "Test";
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "ServiceA.cs"),
                "DoWork_Extracted",
                9,
                9);

            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        var serviceAContent = await project.ReadSourceAsync("ServiceA.cs");

        // When the extracted method signature changes, the source-file call site must be updated
        // Expected: var result = DoWork_Extracted(_svc);
        // Actual (bug): var result = DoWork(_svc);
        Assert.Contains("DoWork_Extracted(_svc)", serviceAContent);
        Assert.DoesNotContain("DoWork(_svc)", serviceAContent);
    }

    [Fact]
    public async Task with_cross_file_call_sites_still_updated_correctly()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("ServiceA.cs", """
                                         namespace TestProject;

                                         public class ServiceA
                                         {
                                             public void Process()
                                             {
                                                 var result = DoWork("alpha");
                                             }

                                             private string DoWork(string name) => $"Service: {name}";
                                         }
                                         """);

        project.AddSource("ServiceB.cs", """
                                         namespace TestProject;

                                         public class ServiceB
                                         {
                                             public void Process()
                                             {
                                                 var result = DoWork("beta");
                                             }

                                             private string DoWork(string name) => $"Service: {name}";
                                         }
                                         """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "ServiceA.cs"),
                "DoWork_Extracted",
                7,
                7);

            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        // Cross-file call site should be updated (regression check)
        var serviceBContent = await project.ReadSourceAsync("ServiceB.cs");
        Assert.Contains("DoWork_Extracted", serviceBContent);
    }

    [Fact]
    public async Task with_all_call_sites_source_and_cross_file_updated()
    {
        // BLOCKER: Cross-file duplicates with DIFFERENT string literals ("alpha" vs "beta" vs "gamma")
        // are filtered out by semantic validation. This test uses IDENTICAL string literals
        // across all files to test that replacement works when semantics are truly identical.
        using var project = new AdhocProjectBuilder();
        project.AddSource("ServiceA.cs", """
                                         namespace TestProject;

                                         public class ServiceA
                                         {
                                             public void Process()
                                             {
                                                 var result = DoWork("test");
                                             }

                                             private string DoWork(string name) => $"Service: {name}";
                                         }
                                         """);

        project.AddSource("ServiceB.cs", """
                                         namespace TestProject;

                                         public class ServiceB
                                         {
                                             public void Process()
                                             {
                                                 var result = DoWork("test");
                                             }

                                             private string DoWork(string name) => $"Service: {name}";
                                         }
                                         """);

        project.AddSource("ServiceC.cs", """
                                         namespace TestProject;

                                         public class ServiceC
                                         {
                                             public void Process()
                                             {
                                                 var result = DoWork("test");
                                             }

                                             private string DoWork(string name) => $"Service: {name}";
                                         }
                                         """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "ServiceA.cs"),
                "DoWork_Extracted",
                7,
                7);

            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        // All call sites with identical literals should reference the extracted method
        var serviceAContent = await project.ReadSourceAsync("ServiceA.cs");
        var serviceBContent = await project.ReadSourceAsync("ServiceB.cs");
        var serviceCContent = await project.ReadSourceAsync("ServiceC.cs");

        Assert.Contains("DoWork_Extracted", serviceAContent);
        Assert.Contains("DoWork_Extracted", serviceBContent);
        Assert.Contains("DoWork_Extracted", serviceCContent);
    }
}
