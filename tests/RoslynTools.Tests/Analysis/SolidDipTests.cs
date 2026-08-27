using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Services;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class SolidDipTests
{
    // -----------------------------------------------------------------------
    // Batch 1: SolidAnalyzer.AnalyzeDipAsync heuristic engine
    // -----------------------------------------------------------------------

    private static async Task<SolidDipReport> AnalyzeSourceAsync(
        string source, string fileName = "Test.cs")
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource(fileName, source);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            return await SolidAnalyzer.AnalyzeDipAsync(sln);
        }
    }

    // ----- ConcreteConstructorParams -----

    [Fact]
    public async Task ctor_with_interface_param_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IService { void Do(); }
            public class GoodClass
            {
                public GoodClass(IService svc) { }
            }
            """);
        report.ConcreteConstructorParams.ShouldBeEmpty();
    }

    [Fact]
    public async Task ctor_with_abstract_class_param_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public abstract class Base { }
            public class GoodClass
            {
                public GoodClass(Base b) { }
            }
            """);
        report.ConcreteConstructorParams.ShouldBeEmpty();
    }

    [Fact]
    public async Task ctor_with_concrete_class_param_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Concrete { }
            public class BadClass
            {
                public BadClass(Concrete c) { }
            }
            """);
        Assert.Single(report.ConcreteConstructorParams);
        var item = report.ConcreteConstructorParams[0];
        item.TypeName.ShouldBe("BadClass");
        item.ParamName.ShouldBe("c");
        item.ConcreteType.ShouldBe("Concrete");
    }

    [Fact]
    public async Task ctor_with_value_type_param_is_not_flagged()
    {
        // int, DateTime, custom struct
        var report = await AnalyzeSourceAsync("""
            public struct MyStruct { }
            public class Cls
            {
                public Cls(int x, System.DateTime when, MyStruct s) { }
            }
            """);
        report.ConcreteConstructorParams.ShouldBeEmpty();
    }

    [Fact]
    public async Task ctor_with_string_param_is_not_flagged()
    {
        // string is a class but is treated as a primitive — exclude System.* types
        var report = await AnalyzeSourceAsync("""
            public class Cls
            {
                public Cls(string name) { }
            }
            """);
        report.ConcreteConstructorParams.ShouldBeEmpty();
    }

    [Fact]
    public async Task ctor_with_record_param_is_not_flagged()
    {
        // Records are DTOs, exclude
        var report = await AnalyzeSourceAsync("""
            public record MyDto(string Name);
            public class Cls
            {
                public Cls(MyDto d) { }
            }
            """);
        report.ConcreteConstructorParams.ShouldBeEmpty();
    }

    [Fact]
    public async Task ctor_with_delegate_param_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Cls
            {
                public Cls(System.Action a) { }
            }
            """);
        report.ConcreteConstructorParams.ShouldBeEmpty();
    }

    [Fact]
    public async Task concrete_ctor_params_are_sorted_by_type_then_param()
    {
        var report = await AnalyzeSourceAsync("""
            public class Concrete { }
            public class Zeta
            {
                public Zeta(Concrete b, Concrete a) { }
            }
            public class Alpha
            {
                public Alpha(Concrete x) { }
            }
            """);
        report.ConcreteConstructorParams.Count.ShouldBe(3);
        report.ConcreteConstructorParams[0].TypeName.ShouldBe("Alpha");
        report.ConcreteConstructorParams[1].TypeName.ShouldBe("Zeta");
        report.ConcreteConstructorParams[1].ParamName.ShouldBe("a");
        report.ConcreteConstructorParams[2].TypeName.ShouldBe("Zeta");
        report.ConcreteConstructorParams[2].ParamName.ShouldBe("b");
    }

    // ----- InlineNewCalls -----

    [Fact]
    public async Task inline_new_of_concrete_class_in_method_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Concrete { }
            public class BadClass
            {
                public void DoIt()
                {
                    var c = new Concrete();
                }
            }
            """);
        Assert.Single(report.InlineNewCalls);
        var item = report.InlineNewCalls[0];
        item.TypeName.ShouldBe("BadClass");
        item.MemberName.ShouldBe("DoIt");
        item.ConcreteType.ShouldBe("Concrete");
    }

    [Fact]
    public async Task inline_new_of_exception_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Cls
            {
                public void DoIt()
                {
                    throw new System.InvalidOperationException("nope");
                }
            }
            """);
        report.InlineNewCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task inline_new_of_value_type_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Cls
            {
                public void DoIt()
                {
                    var d = new System.DateTime(2026, 1, 1);
                }
            }
            """);
        report.InlineNewCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task inline_new_of_record_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public record MyDto(string Name);
            public class Cls
            {
                public void DoIt()
                {
                    var d = new MyDto("hi");
                }
            }
            """);
        report.InlineNewCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task class_named_factory_is_excluded_from_inline_new()
    {
        var report = await AnalyzeSourceAsync("""
            public class Concrete { }
            public class WidgetFactory
            {
                public void Make()
                {
                    var c = new Concrete();
                }
            }
            """);
        report.InlineNewCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task class_named_builder_is_excluded_from_inline_new()
    {
        var report = await AnalyzeSourceAsync("""
            public class Concrete { }
            public class QueryBuilder
            {
                public void Make()
                {
                    var c = new Concrete();
                }
            }
            """);
        report.InlineNewCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task class_named_provider_is_excluded_from_inline_new()
    {
        var report = await AnalyzeSourceAsync("""
            public class Concrete { }
            public class ConfigProvider
            {
                public void Make()
                {
                    var c = new Concrete();
                }
            }
            """);
        report.InlineNewCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task inline_new_in_property_getter_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Concrete { }
            public class Cls
            {
                public Concrete Value => new Concrete();
            }
            """);
        Assert.Single(report.InlineNewCalls);
        report.InlineNewCalls[0].MemberName.ShouldBe("Value");
    }

    [Fact]
    public async Task inline_new_of_string_is_not_flagged()
    {
        // string is BCL, exclude (System.*)
        var report = await AnalyzeSourceAsync("""
            public class Cls
            {
                public void DoIt()
                {
                    var s = new string('x', 3);
                }
            }
            """);
        report.InlineNewCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task inline_new_calls_are_sorted_by_type_then_member()
    {
        var report = await AnalyzeSourceAsync("""
            public class Concrete { }
            public class Zeta
            {
                public void B() { var c = new Concrete(); }
                public void A() { var c = new Concrete(); }
            }
            public class Alpha
            {
                public void X() { var c = new Concrete(); }
            }
            """);
        report.InlineNewCalls.Count.ShouldBe(3);
        report.InlineNewCalls[0].TypeName.ShouldBe("Alpha");
        report.InlineNewCalls[1].TypeName.ShouldBe("Zeta");
        report.InlineNewCalls[1].MemberName.ShouldBe("A");
        report.InlineNewCalls[2].TypeName.ShouldBe("Zeta");
        report.InlineNewCalls[2].MemberName.ShouldBe("B");
    }

    // -----------------------------------------------------------------------
    // Batch 2: Result types + SolidResult.DIP block
    // -----------------------------------------------------------------------

    [Fact]
    public void dip_concrete_ctor_param_item_has_required_fields()
    {
        var item = new DipConcreteCtorParamItem
        {
            TypeName = "BadClass",
            ParamName = "c",
            ConcreteType = "Concrete",
            FilePath = "src/BadClass.cs",
            Line = 10
        };
        item.TypeName.ShouldBe("BadClass");
        item.ParamName.ShouldBe("c");
        item.ConcreteType.ShouldBe("Concrete");
        item.FilePath.ShouldBe("src/BadClass.cs");
        item.Line.ShouldBe(10);
    }

    [Fact]
    public void dip_inline_new_call_item_has_required_fields()
    {
        var item = new DipInlineNewCallItem
        {
            TypeName = "BadClass",
            MemberName = "DoIt",
            ConcreteType = "Concrete",
            FilePath = "src/BadClass.cs",
            Line = 20
        };
        item.TypeName.ShouldBe("BadClass");
        item.MemberName.ShouldBe("DoIt");
        item.ConcreteType.ShouldBe("Concrete");
    }

    [Fact]
    public void solid_result_dip_block_is_empty_by_default()
    {
        var result = new SolidResult();
        result.DIP.ShouldNotBeNull();
        result.DIP.ConcreteConstructorParams.ShouldBeEmpty();
        result.DIP.InlineNewCalls.ShouldBeEmpty();
    }

    [Fact]
    public void solid_result_json_has_top_level_DIP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(StaticAnalyzerResultAdapters.Solid(result), output);

        using var doc = JsonDocument.Parse(output.ToString());
        doc.RootElement.TryGetProperty("DIP", out _).ShouldBeTrue();
    }

    [Fact]
    public void solid_result_json_dip_has_concreteCtorParams_and_inlineNewCalls_arrays()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(StaticAnalyzerResultAdapters.Solid(result), output);

        using var doc = JsonDocument.Parse(output.ToString());
        var dip = doc.RootElement.GetProperty("DIP");
        dip.TryGetProperty("ConcreteConstructorParams", out var ccp).ShouldBeTrue();
        dip.TryGetProperty("InlineNewCalls", out var inc).ShouldBeTrue();
        ccp.ValueKind.ShouldBe(JsonValueKind.Array);
        inc.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    // -----------------------------------------------------------------------
    // Batch 3: SolidCommand CLI wiring for DIP
    // -----------------------------------------------------------------------

    [Fact]
    public async Task execute_async_returns_0_for_valid_dip_principle()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "DIP", 10, 5, 7, 2);
        exitCode.ShouldBe(0);
    }

    [Fact]
    public void principle_help_mentions_DIP()
    {
        var cmd = App.Commands.Single(entry => entry.Info.Name == "solid").Build();
        var principleOpt = cmd.Options.OfType<Option<string>>()
            .First(o => o.Name == "principle");
        Assert.Contains("DIP", principleOpt.Description, StringComparison.Ordinal);
    }
}
