using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ImportDirective
{
    [Fact]
    public async Task analyzes_reports_global_and_static_imports()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("GlobalUsings.cs", """
                                             global using System;
                                             global using static System.Math;
                                             """);
        project.AddSource("App.cs", """
                                    using System.IO;
                                    using Alias = System.Text.StringBuilder;
                                    using static System.Console;

                                    class App { }
                                    """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var all = await new ImportDirectiveService().AnalyzeAsync(solution, "all");
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");
            var statics = await new ImportDirectiveService().AnalyzeAsync(solution, "static");
            var aliases = await new ImportDirectiveService().AnalyzeAsync(solution, "alias");

            all.ShouldContain(i => i.Kind == "global" && i.Name == "System");
            all.ShouldContain(i => i.Kind == "global-static" && i.Name == "System.Math");
            all.ShouldContain(i => i.Kind == "static" && i.Name == "System.Console");
            all.ShouldContain(i => i.Kind == "alias" && i.Alias == "Alias");
            all.ShouldContain(i => i.Kind == "using" && i.Name == "System.IO");
            globals.Count.ShouldBe(2);
            statics.Count.ShouldBe(2);
            aliases.Count.ShouldBe(1);
            aliases[0].Alias.ShouldBe("Alias");
        }
    }

    [Fact]
    public async Task analyzes_skips_generated_and_intermediate_files()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("obj/Debug/ImportApp.GlobalUsings.g.cs", """
                                                                   global using System;
                                                                   """);
        project.AddSource("GlobalUsings.cs", """
                                             global using Xunit;
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");

            var only = Assert.Single(globals);
            only.Name.ShouldBe("Xunit");
        }
    }

    [Fact]
    public async Task analyzes_respects_rtignore_patterns()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("Ignored/IgnoredImports.cs", """
                                                       global using System;
                                                       """);
        project.AddSource("GlobalUsings.cs", """
                                             global using Xunit;
                                             """);
        var (workspace, solution) = project.Build();
        File.WriteAllText(Path.Combine(project.ProjectDir, ".rtignore"), "Ignored");

        using (workspace)
        {
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");

            var only = Assert.Single(globals);
            only.Name.ShouldBe("Xunit");
        }
    }

    [Fact]
    public async Task analyzes_respects_command_scoped_rtignore_patterns()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("Ignored/IgnoredImports.cs", """
                                                       global using System;
                                                       """);
        project.AddSource("GlobalUsings.cs", """
                                             global using Xunit;
                                             """);
        var (workspace, solution) = project.Build();
        File.WriteAllText(Path.Combine(project.ProjectDir, ".rtignore"), "list-imports:Ignored");

        using (workspace)
        {
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");

            var only = Assert.Single(globals);
            only.Name.ShouldBe("Xunit");
        }
    }

    [Fact]
    public async Task adds_static_import_to_existing_file()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("App.cs", """
                                    using System;

                                    class App { }
                                    """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "App.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                false);
        }

        var content = await project.ReadSourceAsync("App.cs");
        content.ShouldContain("using static System.Console;", Case.Sensitive);
    }

    [Fact]
    public async Task adds_global_import_to_existing_file()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("GlobalUsings.cs", """
                                             global using System;
                                             """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "GlobalUsings.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddGlobalUsingAsync(
                solution,
                filePath,
                "System.Text",
                false);
        }

        var content = await project.ReadSourceAsync("GlobalUsings.cs");
        content.ShouldContain("global using System.Text;", Case.Sensitive);
    }

    [Fact]
    public async Task does_not_duplicate_existing_imports()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("GlobalUsings.cs", """
                                             global using System;
                                             using static System.Console;
                                             """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "GlobalUsings.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddGlobalUsingAsync(
                solution,
                filePath,
                "System",
                false);

            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                false);
        }

        var content = await project.ReadSourceAsync("GlobalUsings.cs");
        CountOccurrences(content, "global using System;").ShouldBe(1);
        CountOccurrences(content, "using static System.Console;").ShouldBe(1);
    }

    [Fact]
    public async Task add_static_import_with_simplify_strips_qualifiers()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("App.cs", """
                                    using System;

                                    class App
                                    {
                                        /// <see cref="System.Console.WriteLine"/>
                                        void M()
                                        {
                                            Console.WriteLine("x");
                                            Console.WriteLine("y");
                                        }
                                    }
                                    """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "App.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                dryRun: false,
                simplify: true);
        }

        var content = await project.ReadSourceAsync("App.cs");
        content.ShouldContain("using static System.Console;", Case.Sensitive);
        CountOccurrences(content, "Console.WriteLine").ShouldBe(1, "only the <see cref> doc-comment should retain the qualifier");
        CountOccurrences(content, "WriteLine(\"x\")").ShouldBe(1);
        CountOccurrences(content, "WriteLine(\"y\")").ShouldBe(1);
    }

    [Fact]
    public async Task add_static_import_with_simplify_preserves_qualifier_when_local_shadows()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("App.cs", """
                                    using System;

                                    class App
                                    {
                                        void WriteLine(string s) { }

                                        void M()
                                        {
                                            WriteLine("local");
                                            Console.WriteLine("static");
                                        }
                                    }
                                    """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "App.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                dryRun: false,
                simplify: true);
        }

        var content = await project.ReadSourceAsync("App.cs");
        content.ShouldContain("using static System.Console;", Case.Sensitive);
        content.ShouldContain("Console.WriteLine(\"static\")", Case.Sensitive);

        var verifyProject = new AdhocProjectBuilder("VerifyApp");
        verifyProject.AddSource("App.cs", content);
        var (verifyWorkspace, verifySolution) = verifyProject.Build();
        using (verifyWorkspace)
        {
            var verifyDoc = verifySolution.Projects.Single().Documents.Single();
            var compilation = await verifyDoc.Project.GetCompilationAsync();
            var diagnostics = compilation!.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            diagnostics.ShouldBeEmpty();
        }

        verifyProject.Dispose();
    }

    [Fact]
    public async Task add_static_import_with_simplify_is_idempotent_when_directive_already_present()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("App.cs", """
                                    using System;
                                    using static System.Console;

                                    class App
                                    {
                                        void M()
                                        {
                                            Console.WriteLine("hand-edited");
                                            WriteLine("already-shortened");
                                        }
                                    }
                                    """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "App.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                dryRun: false,
                simplify: true);
        }

        var content = await project.ReadSourceAsync("App.cs");
        CountOccurrences(content, "using static System.Console;").ShouldBe(1, "the directive must not be duplicated");
        CountOccurrences(content, "Console.WriteLine").ShouldBe(0, "leftover qualifier must still be stripped on the second pass");
        CountOccurrences(content, "WriteLine(\"hand-edited\")").ShouldBe(1);
        CountOccurrences(content, "WriteLine(\"already-shortened\")").ShouldBe(1);
    }

    [Fact]
    public async Task add_static_import_with_simplify_all_static_imports_reduces_pre_existing_directives()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("App.cs", """
                                    using System;
                                    using static System.Math;

                                    class App
                                    {
                                        double M()
                                        {
                                            Console.WriteLine("hi");
                                            return Math.PI + Math.Sin(1.0);
                                        }
                                    }
                                    """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "App.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                dryRun: false,
                simplify: false,
                simplifyAllStaticImports: true);
        }

        var content = await project.ReadSourceAsync("App.cs");
        content.ShouldContain("using static System.Console;", Case.Sensitive);
        content.ShouldContain("using static System.Math;", Case.Sensitive);
        CountOccurrences(content, "Console.WriteLine").ShouldBe(0, "newly-added Console import should have its qualifiers stripped");
        CountOccurrences(content, "Math.PI").ShouldBe(0, "pre-existing Math static import should also have its qualifiers stripped");
        CountOccurrences(content, "Math.Sin").ShouldBe(0);
        content.ShouldContain("PI + Sin(1.0)", Case.Sensitive);
    }

    [Fact]
    public async Task add_static_import_without_simplify_leaves_qualifiers_alone()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("App.cs", """
                                    using System;

                                    class App
                                    {
                                        void M()
                                        {
                                            Console.WriteLine("preserved");
                                        }
                                    }
                                    """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "App.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                dryRun: false);
        }

        var content = await project.ReadSourceAsync("App.cs");
        content.ShouldContain("using static System.Console;", Case.Sensitive);
        content.ShouldContain("Console.WriteLine(\"preserved\")", Case.Sensitive);
    }

    [Fact]
    public async Task analyzes_reports_line_numbers_from_directive_position()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("GlobalUsings.cs", """
                                             global using System;

                                             global using Xunit;
                                             """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");

            var system = globals.Single(g => g.Name == "System");
            var xunit = globals.Single(g => g.Name == "Xunit");

            system.Line.ShouldBe(1);
            xunit.Line.ShouldBe(3);
        }
    }

    [Fact]
    public async Task analyzes_orders_results_by_file_path_then_line()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("Z_Imports.cs", """
                                          global using Xunit;
                                          """);
        project.AddSource("A_Imports.cs", """
                                          global using System;
                                          global using System.Text;
                                          """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");

            globals.Count.ShouldBe(3);
            globals[0].Name.ShouldBe("System");
            globals[1].Name.ShouldBe("System.Text");
            globals[2].Name.ShouldBe("Xunit");
        }
    }

    [Fact]
    public async Task analyze_with_invalid_kind_throws_argument_exception()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("GlobalUsings.cs", "global using System;");
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var ex = await Should.ThrowAsync<ArgumentException>(() =>
                new ImportDirectiveService().AnalyzeAsync(solution, "bogus"));
            ex.Message.ShouldContain("Kind must be one of");
        }
    }

    [Fact]
    public async Task add_static_throws_when_file_not_in_solution()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("App.cs", "class App { }");
        var (workspace, solution) = project.Build();
        var bogusPath = Path.Combine(project.ProjectDir, "DoesNotExist.cs");

        using (workspace)
        {
            var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
                new ImportDirectiveService().AddStaticUsingAsync(
                    solution, bogusPath, "System.Console", false));
            ex.Message.ShouldContain("not found in solution");
        }
    }

    [Fact]
    public async Task add_global_when_target_file_has_non_global_same_name()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("Imports.cs", """
                                        using System.Text;
                                        """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "Imports.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddGlobalUsingAsync(
                solution, filePath, "System.Text", false);
        }

        var content = await project.ReadSourceAsync("Imports.cs");
        content.ShouldContain("global using System.Text;", Case.Sensitive);
        content.ShouldContain("using System.Text;", Case.Sensitive);
    }

    [Fact]
    public async Task add_global_inserts_after_existing_globals_and_before_non_globals()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("Imports.cs", """
                                        global using System;
                                        global using System.Linq;
                                        using System.Text;
                                        """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "Imports.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddGlobalUsingAsync(
                solution, filePath, "System.Collections.Generic", false);
        }

        var content = await project.ReadSourceAsync("Imports.cs");
        var idxLinq = content.IndexOf("global using System.Linq;", StringComparison.Ordinal);
        var idxNew = content.IndexOf("global using System.Collections.Generic;", StringComparison.Ordinal);
        var idxText = content.IndexOf("using System.Text;", StringComparison.Ordinal);

        idxLinq.ShouldBeGreaterThanOrEqualTo(0);
        idxNew.ShouldBeGreaterThanOrEqualTo(0);
        idxText.ShouldBeGreaterThanOrEqualTo(0);
        idxLinq.ShouldBeLessThan(idxNew, "new global must come after existing globals");
        idxNew.ShouldBeLessThan(idxText, "new global must come before non-global usings");
    }

    [Fact]
    public async Task add_static_appends_after_all_existing_usings()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("Imports.cs", """
                                        global using System;
                                        global using System.Linq;
                                        using System.Text;
                                        """);
        var (workspace, solution) = project.Build();
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.Directory);
        var filePath = Path.Combine(project.ProjectDir, "Imports.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddStaticUsingAsync(
                solution, filePath, "System.Console", false);
        }

        var content = await project.ReadSourceAsync("Imports.cs");
        var idxText = content.IndexOf("using System.Text;", StringComparison.Ordinal);
        var idxNewStatic = content.IndexOf("using static System.Console;", StringComparison.Ordinal);

        idxText.ShouldBeGreaterThanOrEqualTo(0);
        idxNewStatic.ShouldBeGreaterThanOrEqualTo(0);
        idxText.ShouldBeLessThan(idxNewStatic, "new static using must be appended after all existing usings");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
