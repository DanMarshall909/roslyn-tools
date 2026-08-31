using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Services;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class SolidSrpTests
{
    // -----------------------------------------------------------------------
    // Batch 2: SolidAnalyzer.AnalyzeSrpAsync heuristic engine
    // -----------------------------------------------------------------------

    private static async Task<SolidSrpReport> AnalyzeSourceAsync(
        string source, int minMethods = 10, int minDeps = 5,
        string fileName = "Test.cs")
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource(fileName, source);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            return await SolidAnalyzer.AnalyzeSrpAsync(sln, minMethods, minDeps);
        }
    }

    [Fact]
    public async Task class_below_both_thresholds_is_excluded()
    {
        // 2 methods, 1 dep — both below defaults (10/5)
        var report = await AnalyzeSourceAsync("""
            public class ServiceA
            {
                private readonly object _dep;
                public ServiceA(object dep) { _dep = dep; }
                public void MethodOne() { }
                public void MethodTwo() { }
            }
            """);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task class_meeting_only_method_threshold_is_excluded()
    {
        // 10 methods but 0 deps — must meet BOTH
        var report = await AnalyzeSourceAsync("""
            public class BigButNoDeps
            {
                public void M1() { }
                public void M2() { }
                public void M3() { }
                public void M4() { }
                public void M5() { }
                public void M6() { }
                public void M7() { }
                public void M8() { }
                public void M9() { }
                public void M10() { }
            }
            """);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task class_meeting_only_dep_threshold_is_excluded()
    {
        // 5 deps but only 2 public methods — must meet BOTH
        var report = await AnalyzeSourceAsync("""
            public class FewMethodsManyDeps
            {
                public FewMethodsManyDeps(object a, object b, object c, object d, object e) { }
                public void M1() { }
                public void M2() { }
            }
            """);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task class_meeting_both_thresholds_is_included()
    {
        // 10 public methods + 5 distinct deps → violation
        var report = await AnalyzeSourceAsync("""
            public class BadClass
            {
                public BadClass(
                    System.Text.StringBuilder a,
                    System.IO.Stream b,
                    System.Collections.Generic.List<int> c,
                    System.Exception d,
                    System.Uri e) { }
                public void M1() { }
                public void M2() { }
                public void M3() { }
                public void M4() { }
                public void M5() { }
                public void M6() { }
                public void M7() { }
                public void M8() { }
                public void M9() { }
                public void M10() { }
            }
            """);
        Assert.Single(report.Violations);
        report.Violations[0].TypeName.ShouldBe("BadClass");
    }

    [Fact]
    public async Task class_at_exact_threshold_on_both_is_included()
    {
        // exactly minMethods=3, minDeps=2 (use low thresholds)
        var report = await AnalyzeSourceAsync("""
            public class ExactClass
            {
                public ExactClass(System.Text.StringBuilder a, System.IO.Stream b) { }
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """, minMethods: 3, minDeps: 2);
        Assert.Single(report.Violations);
        report.Violations[0].TypeName.ShouldBe("ExactClass");
    }

    [Fact]
    public async Task abstract_class_is_excluded()
    {
        var report = await AnalyzeSourceAsync("""
            public abstract class AbstractBase
            {
                protected AbstractBase(
                    System.Text.StringBuilder a, System.IO.Stream b) { }
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """, minMethods: 2, minDeps: 2);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task static_class_is_excluded()
    {
        var report = await AnalyzeSourceAsync("""
            public static class StaticHelper
            {
                public static void M1() { }
                public static void M2() { }
                public static void M3() { }
            }
            """, minMethods: 2, minDeps: 0);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task interface_is_excluded()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IMyService
            {
                void M1();
                void M2();
                void M3();
            }
            """, minMethods: 2, minDeps: 0);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task record_type_is_excluded()
    {
        var report = await AnalyzeSourceAsync("""
            public record MyRecord(string A, string B, string C)
            {
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """, minMethods: 2, minDeps: 0);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task class_with_no_constructor_has_zero_deps()
    {
        // No constructor at all — zero deps, even if methods >= threshold
        var report = await AnalyzeSourceAsync("""
            public class NoCtor
            {
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """, minMethods: 3, minDeps: 1);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task implicit_default_ctor_has_zero_dep_count()
    {
        // Compiler-generated default ctor is IsImplicitlyDeclared — zero deps
        var report = await AnalyzeSourceAsync("""
            public class ImplicitCtorClass
            {
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """, minMethods: 3, minDeps: 1);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task duplicate_param_types_counted_as_one_dep()
    {
        // Two params of the same type → only 1 distinct dep
        var report = await AnalyzeSourceAsync("""
            public class DuplicateTypes
            {
                public DuplicateTypes(System.Text.StringBuilder a, System.Text.StringBuilder b) { }
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """, minMethods: 3, minDeps: 2);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task class_with_multiple_ctors_uses_the_one_with_most_params()
    {
        // Ctor1 has 1 param, Ctor2 has 5 params — uses Ctor2 for dep count
        var report = await AnalyzeSourceAsync("""
            public class MultiCtor
            {
                public MultiCtor(object a) { }
                public MultiCtor(
                    System.Text.StringBuilder a,
                    System.IO.Stream b,
                    System.Collections.Generic.List<int> c,
                    System.Exception d,
                    System.Uri e) { }
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """, minMethods: 3, minDeps: 5);
        Assert.Single(report.Violations);
    }

    [Fact]
    public async Task primary_constructor_deps_are_counted()
    {
        // C# 12 primary constructor — deps must come from symbol not syntax
        var report = await AnalyzeSourceAsync("""
            public class PrimaryCtorClass(
                System.Text.StringBuilder a,
                System.IO.Stream b,
                System.Collections.Generic.List<int> c,
                System.Exception d,
                System.Uri e)
            {
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """, minMethods: 3, minDeps: 5);
        Assert.Single(report.Violations);
    }

    [Fact]
    public async Task generated_file_classes_are_excluded()
    {
        // .g.cs file — classes inside must be skipped
        var report = await AnalyzeSourceAsync("""
            public class GeneratedClass
            {
                public GeneratedClass(
                    System.Text.StringBuilder a,
                    System.IO.Stream b,
                    System.Collections.Generic.List<int> c,
                    System.Exception d,
                    System.Uri e) { }
                public void M1() { }
                public void M2() { }
                public void M3() { }
                public void M4() { }
                public void M5() { }
                public void M6() { }
                public void M7() { }
                public void M8() { }
                public void M9() { }
                public void M10() { }
            }
            """, fileName: "Generated.g.cs");
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task partial_class_across_two_files_counted_only_once()
    {
        // Two partial declarations for the same class — must appear exactly once in violations
        using var project = new AdhocProjectBuilder();
        project.AddSource("PartA.cs", """
            public partial class PartialViolator
            {
                public PartialViolator(
                    System.Text.StringBuilder a,
                    System.IO.Stream b,
                    System.Collections.Generic.List<int> c,
                    System.Exception d,
                    System.Uri e) { }
                public void M1() { }
                public void M2() { }
                public void M3() { }
                public void M4() { }
                public void M5() { }
            }
            """);
        project.AddSource("PartB.cs", """
            public partial class PartialViolator
            {
                public void M6() { }
                public void M7() { }
                public void M8() { }
                public void M9() { }
                public void M10() { }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await SolidAnalyzer.AnalyzeSrpAsync(sln, minMethods: 10, minDeps: 5);
            Assert.Single(report.Violations);
        }
    }

    [Fact]
    public async Task detail_string_describes_method_and_dep_counts()
    {
        var report = await AnalyzeSourceAsync("""
            public class DetailedClass
            {
                public DetailedClass(
                    System.Text.StringBuilder a,
                    System.IO.Stream b,
                    System.Collections.Generic.List<int> c,
                    System.Exception d,
                    System.Uri e) { }
                public void M1() { }
                public void M2() { }
                public void M3() { }
                public void M4() { }
                public void M5() { }
                public void M6() { }
                public void M7() { }
                public void M8() { }
                public void M9() { }
                public void M10() { }
            }
            """);
        Assert.Single(report.Violations);
        report.Violations[0].Detail.ShouldBe("10 public methods, 5 injected dependencies");
    }

    [Fact]
    public void command_info_category_is_analyze()
    {
        SolidCommand.Info.Category.ShouldBe("analyze");
    }

    [Fact]
    public void command_info_json_output_is_true()
    {
        SolidCommand.Info.JsonOutput.ShouldBeTrue();
    }

    [Fact]
    public void command_info_mutates_is_false()
    {
        SolidCommand.Info.Mutates.ShouldBeFalse();
    }

    [Fact]
    public void command_info_supported_formats_includes_text_and_json()
    {
        var formats = SolidCommand.Info.SupportedFormats;
        formats.ShouldNotBeNull();
        formats.ShouldContain(OutputFormat.Text);
        formats.ShouldContain(OutputFormat.Json);
    }

    [Fact]
    public void srp_violation_item_has_required_fields()
    {
        var item = new SrpViolationItem
        {
            TypeName = "MyClass",
            FilePath = "src/MyClass.cs",
            Line = 10,
            PublicMethodCount = 7,
            DependencyCount = 4,
            Detail = "Too many responsibilities"
        };

        item.TypeName.ShouldBe("MyClass");
        item.FilePath.ShouldBe("src/MyClass.cs");
        item.Line.ShouldBe(10);
        item.PublicMethodCount.ShouldBe(7);
        item.DependencyCount.ShouldBe(4);
        item.Detail.ShouldBe("Too many responsibilities");
    }

    [Fact]
    public void solid_result_srp_list_is_empty_by_default()
    {
        var result = new SolidResult();
        result.SRP.ShouldBeEmpty();
    }

    [Fact]
    public void solid_result_json_has_top_level_SRP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(StaticAnalyzerResultAdapters.Solid(result), output);

        using var doc = JsonDocument.Parse(output.ToString());
        doc.RootElement.TryGetProperty("SRP", out _).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------
    // Batch 3: SolidCommand CLI wiring — options, validation, ExecuteAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task execute_async_returns_1_for_negative_min_methods()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "SRP", -1, 5, 7, 2);
        exitCode.ShouldBe(1);
    }

    [Fact]
    public async Task execute_async_returns_1_for_negative_min_deps()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "SRP", 10, -1, 7, 2);
        exitCode.ShouldBe(1);
    }

    [Fact]
    public async Task execute_async_returns_1_for_invalid_principle()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "ZZZ", 10, 5, 7, 2);
        exitCode.ShouldBe(1);
    }

    [Fact]
    public async Task execute_async_returns_0_for_valid_srp_principle()
    {
        using var trust = RepositoryTrustContext.UseExplicitTrust();
        var exitCode = await SolidCommand.ExecuteAsync(null, "SRP", 10, 5, 7, 2);
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task execute_async_returns_0_for_all_principle()
    {
        using var trust = RepositoryTrustContext.UseExplicitTrust();
        var exitCode = await SolidCommand.ExecuteAsync(null, "all", 10, 5, 7, 2);
        exitCode.ShouldBe(0);
    }

    [Fact]
    public void execute_async_defaults_min_methods_to_10()
    {
        var cmd = App.Commands.Single(entry => entry.Info.Name == "solid").Build();
        var minMethodsOpt = cmd.Options.OfType<Option<int>>()
            .First(o => o.Name == "min-methods");
        minMethodsOpt.Parse("").GetValueForOption(minMethodsOpt).ShouldBe(10);
    }

    [Fact]
    public void execute_async_defaults_min_deps_to_5()
    {
        var cmd = App.Commands.Single(entry => entry.Info.Name == "solid").Build();
        var minDepsOpt = cmd.Options.OfType<Option<int>>()
            .First(o => o.Name == "min-deps");
        minDepsOpt.Parse("").GetValueForOption(minDepsOpt).ShouldBe(5);
    }
}
