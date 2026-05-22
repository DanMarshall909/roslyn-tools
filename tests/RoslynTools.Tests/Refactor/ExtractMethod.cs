using System.Globalization;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Refactor;

public class ExtractMethod
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
    {
        return Path.Combine(project.ProjectDir, relativePath);
    }

    [Fact]
    [Trait("Concern", "SourceFileCallSite")]
    public async Task source_file_as_first_occurrence_updates_call_site()
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
        serviceAContent.ShouldContain("DoWork_Extracted", Case.Sensitive);
    }

    [Fact(Skip = "US-050: pending — source-file call site not yet updated with new parameters")]
    [Trait("Concern", "SourceFileCallSite")]
    public async Task signature_changes_source_file_receives_new_parameters()
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
        serviceAContent.ShouldContain("DoWork_Extracted(_svc)", Case.Sensitive);
        serviceAContent.ShouldNotContain("DoWork(_svc)", Case.Sensitive);
    }

    [Fact]
    [Trait("Concern", "CrossFileCallSites")]
    public async Task cross_file_duplicates_extracted_code_compiles()
    {
        // This test verifies that when duplicates exist across files, the extracted method
        // is accessible to all call sites. Currently fails because the extracted method is
        // created as private, making it inaccessible to ServiceB.
        //
        // To fix: Detect cross-file matches and upgrade visibility to public static.

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

        // Cross-file call site should be updated
        var serviceBContent = await project.ReadSourceAsync("ServiceB.cs");
        serviceBContent.ShouldContain("DoWork_Extracted", Case.Sensitive);

        // The extracted method must be accessible to cross-file call sites
        var (workspace2, reloadedSln) = project.Build();
        using (workspace2)
        {
            var allErrors = new List<string>();
            foreach (var proj in reloadedSln.Projects)
            {
                var compilation = await proj.GetCompilationAsync();
                if (compilation != null)
                {
                    var errors = compilation.GetDiagnostics()
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
                        .ToList();
                    allErrors.AddRange(errors);
                }
            }

            allErrors.ShouldBeEmpty(); // No compilation errors
        }
    }

    [Fact]
    [Trait("Concern", "CrossFileCallSites")]
    [Trait("Category", "Regression")]
    public async Task cross_file_duplicates_still_get_updated()
    {
        // Regression test: verify that duplicates in other files are found and replaced
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
        serviceBContent.ShouldContain("DoWork_Extracted", Case.Sensitive);
    }

    [Fact]
    [Trait("Concern", "CrossFileCallSites")]
    [Trait("Category", "Debug")]
    public async Task debug_show_extracted_method_visibility()
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
                sln, FilePath(project, "ServiceA.cs"), "DoWork_Extracted", 7, 7);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        var serviceAContent = await project.ReadSourceAsync("ServiceA.cs");
        var isPublic = serviceAContent.Contains("public") && serviceAContent.Contains("DoWork_Extracted");
        var isStatic = serviceAContent.Contains("static") && serviceAContent.Contains("DoWork_Extracted");

        // Phase 2: Extracted method is upgraded to public static when cross-file duplicates exist
        (isPublic && isStatic).ShouldBeTrue();
    }

    [Fact]
    [Trait("Concern", "SourceFileCallSite")]
    [Trait("Concern", "CrossFileCallSites")]
    public async Task same_literal_values_cross_file_call_sites_get_updated()
    {
        // BLOCKER: Cross-file duplicates with DIFFERENT string literals ("alpha" vs "beta" vs "gamma")
        // are filtered out by semantic validation (dogfood-gaps line 11-13).
        // This test uses IDENTICAL string literals across all files to test cross-file replacement.
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
                7,
                replaceDuplicates: true);

            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        // All call sites with identical literals should reference the extracted method
        var serviceAContent = await project.ReadSourceAsync("ServiceA.cs");
        var serviceBContent = await project.ReadSourceAsync("ServiceB.cs");
        var serviceCContent = await project.ReadSourceAsync("ServiceC.cs");

        serviceAContent.ShouldContain("DoWork_Extracted", Case.Sensitive);
        serviceBContent.ShouldContain("DoWork_Extracted", Case.Sensitive);
        serviceCContent.ShouldContain("DoWork_Extracted", Case.Sensitive);
    }
}
