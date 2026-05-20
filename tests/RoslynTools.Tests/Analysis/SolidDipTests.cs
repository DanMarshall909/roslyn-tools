using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

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
        Assert.Empty(report.ConcreteConstructorParams);
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
        Assert.Empty(report.ConcreteConstructorParams);
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
        Assert.Equal("BadClass", item.TypeName);
        Assert.Equal("c", item.ParamName);
        Assert.Equal("Concrete", item.ConcreteType);
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
        Assert.Empty(report.ConcreteConstructorParams);
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
        Assert.Empty(report.ConcreteConstructorParams);
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
        Assert.Empty(report.ConcreteConstructorParams);
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
        Assert.Empty(report.ConcreteConstructorParams);
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
        Assert.Equal(3, report.ConcreteConstructorParams.Count);
        Assert.Equal("Alpha", report.ConcreteConstructorParams[0].TypeName);
        Assert.Equal("Zeta", report.ConcreteConstructorParams[1].TypeName);
        Assert.Equal("a", report.ConcreteConstructorParams[1].ParamName);
        Assert.Equal("Zeta", report.ConcreteConstructorParams[2].TypeName);
        Assert.Equal("b", report.ConcreteConstructorParams[2].ParamName);
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
        Assert.Equal("BadClass", item.TypeName);
        Assert.Equal("DoIt", item.MemberName);
        Assert.Equal("Concrete", item.ConcreteType);
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
        Assert.Empty(report.InlineNewCalls);
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
        Assert.Empty(report.InlineNewCalls);
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
        Assert.Empty(report.InlineNewCalls);
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
        Assert.Empty(report.InlineNewCalls);
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
        Assert.Empty(report.InlineNewCalls);
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
        Assert.Empty(report.InlineNewCalls);
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
        Assert.Equal("Value", report.InlineNewCalls[0].MemberName);
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
        Assert.Empty(report.InlineNewCalls);
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
        Assert.Equal(3, report.InlineNewCalls.Count);
        Assert.Equal("Alpha", report.InlineNewCalls[0].TypeName);
        Assert.Equal("Zeta", report.InlineNewCalls[1].TypeName);
        Assert.Equal("A", report.InlineNewCalls[1].MemberName);
        Assert.Equal("Zeta", report.InlineNewCalls[2].TypeName);
        Assert.Equal("B", report.InlineNewCalls[2].MemberName);
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
        Assert.Equal("BadClass", item.TypeName);
        Assert.Equal("c", item.ParamName);
        Assert.Equal("Concrete", item.ConcreteType);
        Assert.Equal("src/BadClass.cs", item.FilePath);
        Assert.Equal(10, item.Line);
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
        Assert.Equal("BadClass", item.TypeName);
        Assert.Equal("DoIt", item.MemberName);
        Assert.Equal("Concrete", item.ConcreteType);
    }

    [Fact]
    public void solid_result_dip_block_is_empty_by_default()
    {
        var result = new SolidResult();
        Assert.NotNull(result.DIP);
        Assert.Empty(result.DIP.ConcreteConstructorParams);
        Assert.Empty(result.DIP.InlineNewCalls);
    }

    [Fact]
    public void solid_result_json_has_top_level_DIP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(result, output);

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.True(doc.RootElement.TryGetProperty("DIP", out _));
    }

    [Fact]
    public void solid_result_json_dip_has_concreteCtorParams_and_inlineNewCalls_arrays()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(result, output);

        using var doc = JsonDocument.Parse(output.ToString());
        var dip = doc.RootElement.GetProperty("DIP");
        Assert.True(dip.TryGetProperty("ConcreteConstructorParams", out var ccp));
        Assert.True(dip.TryGetProperty("InlineNewCalls", out var inc));
        Assert.Equal(JsonValueKind.Array, ccp.ValueKind);
        Assert.Equal(JsonValueKind.Array, inc.ValueKind);
    }

    // -----------------------------------------------------------------------
    // Batch 3: SolidCommand CLI wiring for DIP
    // -----------------------------------------------------------------------

    [Fact]
    public async Task execute_async_returns_0_for_valid_dip_principle()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "DIP", 10, 5, 7);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void principle_help_mentions_DIP()
    {
        var cmd = SolidCommand.Build();
        var principleOpt = cmd.Options.OfType<Option<string>>()
            .First(o => o.Name == "principle");
        Assert.Contains("DIP", principleOpt.Description, StringComparison.Ordinal);
    }
}
