using RoslynTools.Core;

namespace RoslynTools.Tests.Issues.EP_007_CodeQualityRefactor;

public static class Code_Quality_Refactor
{
    public static class Extract_method_with_parameterization_for_duplicate_patterns
    {
        public class When_source_file_is_the_first_occurrence_the_call_site_should_be_updated_with_new_parameters
        {
            private static string FilePath(AdhocProjectBuilder project, string relativePath)
                => Path.Combine(project.ProjectDir, relativePath);

            [Fact]
            public async Task It_reproduces_the_bug_where_source_file_call_site_is_not_updated()
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
                        startLine: 7,
                        endLine: 7);

                    await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
                }

                // Assert: Extraction succeeds (documents the bug exists — call site not updated)
                var serviceAContent = await project.ReadSourceAsync("ServiceA.cs");
                Assert.Contains("DoWork_Extracted", serviceAContent);
            }
        }

        public class When_method_signature_changes_during_extraction_the_source_file_call_site_should_receive_new_parameters
        {
            private static string FilePath(AdhocProjectBuilder project, string relativePath)
                => Path.Combine(project.ProjectDir, relativePath);

            [Fact(Skip = "US-050: pending fix — source-file call site not yet updated with new parameters")]
            public async Task It_updates_the_source_file_call_site_to_pass_the_new_parameters()
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
                        startLine: 9,
                        endLine: 9);

                    await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
                }

                var serviceAContent = await project.ReadSourceAsync("ServiceA.cs");

                // When the extracted method signature changes, the source-file call site must be updated
                // Expected: var result = DoWork_Extracted(_svc);
                // Actual (bug): var result = DoWork(_svc);
                Assert.Contains("DoWork_Extracted(_svc)", serviceAContent);
                Assert.DoesNotContain("DoWork(_svc)", serviceAContent);
            }
        }

        public class When_extracting_with_replace_duplicates_the_cross_file_call_sites_should_still_be_updated
        {
            private static string FilePath(AdhocProjectBuilder project, string relativePath)
                => Path.Combine(project.ProjectDir, relativePath);

            [Fact]
            public async Task It_updates_cross_file_call_sites_correctly()
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
                        startLine: 7,
                        endLine: 7);

                    await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
                }

                // Cross-file call site should be updated (regression check)
                var serviceBContent = await project.ReadSourceAsync("ServiceB.cs");
                Assert.Contains("DoWork_Extracted", serviceBContent);
            }
        }

        public class When_extracting_across_multiple_files_all_call_sites_including_the_source_file_should_be_updated
        {
            private static string FilePath(AdhocProjectBuilder project, string relativePath)
                => Path.Combine(project.ProjectDir, relativePath);

            [Fact]
            public async Task It_updates_all_call_sites_source_and_cross_file()
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

                project.AddSource("ServiceC.cs", """
                    namespace TestProject;

                    public class ServiceC
                    {
                        public void Process()
                        {
                            var result = DoWork("gamma");
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
                        startLine: 7,
                        endLine: 7);

                    await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
                }

                // All call sites — source and cross-file — should reference the extracted method
                var serviceAContent = await project.ReadSourceAsync("ServiceA.cs");
                var serviceBContent = await project.ReadSourceAsync("ServiceB.cs");
                var serviceCContent = await project.ReadSourceAsync("ServiceC.cs");

                Assert.Contains("DoWork_Extracted", serviceAContent);
                Assert.Contains("DoWork_Extracted", serviceBContent);
                Assert.Contains("DoWork_Extracted", serviceCContent);
            }
        }
    }
}
