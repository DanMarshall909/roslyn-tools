using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Services;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class SolidIspTests
{
    // -----------------------------------------------------------------------
    // Batch 1: SolidAnalyzer.AnalyzeIspAsync heuristic engine
    // -----------------------------------------------------------------------

    private static async Task<SolidIspReport> AnalyzeSourceAsync(
        string source, int minMembers = 7,
        string fileName = "Test.cs")
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource(fileName, source);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            return await SolidAnalyzer.AnalyzeIspAsync(sln, minMembers);
        }
    }

    // ----- FatInterfaces -----

    [Fact]
    public async Task interface_below_threshold_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public interface ISmall
            {
                void M1();
                void M2();
                void M3();
            }
            """);
        report.FatInterfaces.ShouldBeEmpty();
    }

    [Fact]
    public async Task interface_meeting_threshold_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IBig
            {
                void M1();
                void M2();
                void M3();
                void M4();
                void M5();
                void M6();
                void M7();
            }
            """);
        Assert.Single(report.FatInterfaces);
        report.FatInterfaces[0].InterfaceName.ShouldBe("IBig");
        report.FatInterfaces[0].MemberCount.ShouldBe(7);
    }

    [Fact]
    public async Task fat_interface_member_count_includes_properties_and_events()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IMixed
            {
                void M1();
                void M2();
                int Prop1 { get; }
                int Prop2 { get; set; }
                event System.EventHandler Ev1;
                event System.EventHandler Ev2;
                void M3();
            }
            """);
        Assert.Single(report.FatInterfaces);
        report.FatInterfaces[0].MemberCount.ShouldBe(7);
    }

    [Fact]
    public async Task fat_interface_only_counts_declared_members_not_inherited()
    {
        // IExtra adds 1 member (M2); inherits 6 from IBase — declared count is 1, not 7
        var report = await AnalyzeSourceAsync("""
            public interface IBase
            {
                void B1();
                void B2();
                void B3();
                void B4();
                void B5();
                void B6();
            }
            public interface IExtra : IBase
            {
                void M2();
            }
            """);
        // Only IBase qualifies as fat (6 declared, below default 7) — neither is flagged at default
        report.FatInterfaces.ShouldBeEmpty();

        // Lower threshold so IBase qualifies; verify IExtra is not lifted by inherited members
        var report2 = await AnalyzeSourceAsync("""
            public interface IBase
            {
                void B1();
                void B2();
                void B3();
                void B4();
                void B5();
                void B6();
            }
            public interface IExtra : IBase
            {
                void M2();
            }
            """, minMembers: 6);
        Assert.Single(report2.FatInterfaces);
        report2.FatInterfaces[0].InterfaceName.ShouldBe("IBase");
        report2.FatInterfaces[0].MemberCount.ShouldBe(6);
    }

    [Fact]
    public async Task fat_interface_records_file_and_line()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IBig
            {
                void M1();
                void M2();
                void M3();
                void M4();
                void M5();
                void M6();
                void M7();
            }
            """, fileName: "Foo.cs");
        Assert.Single(report.FatInterfaces);
        report.FatInterfaces[0].FilePath.ShouldEndWith("Foo.cs", Case.Sensitive);
        report.FatInterfaces[0].Line.ShouldBe(1);
    }

    [Fact]
    public async Task fat_interfaces_are_sorted_by_name()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IZebra
            {
                void M1(); void M2(); void M3();
                void M4(); void M5(); void M6(); void M7();
            }
            public interface IApple
            {
                void M1(); void M2(); void M3();
                void M4(); void M5(); void M6(); void M7();
            }
            """);
        report.FatInterfaces.Count.ShouldBe(2);
        report.FatInterfaces[0].InterfaceName.ShouldBe("IApple");
        report.FatInterfaces[1].InterfaceName.ShouldBe("IZebra");
    }

    // ----- UnimplementedMembers -----

    [Fact]
    public async Task class_throwing_not_implemented_on_interface_method_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                void DoIt();
                void DoOther();
            }
            public class BadImpl : IService
            {
                public void DoIt() => throw new System.NotImplementedException();
                public void DoOther() { /* real impl */ }
            }
            """);
        Assert.Single(report.UnimplementedMembers);
        var item = report.UnimplementedMembers[0];
        item.TypeName.ShouldBe("BadImpl");
        item.MemberName.ShouldBe("DoIt");
        item.InterfaceName.ShouldBe("IService");
    }

    [Fact]
    public async Task class_throwing_not_supported_on_interface_method_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                void DoIt();
            }
            public class BadImpl : IService
            {
                public void DoIt() => throw new System.NotSupportedException();
            }
            """);
        Assert.Single(report.UnimplementedMembers);
        report.UnimplementedMembers[0].MemberName.ShouldBe("DoIt");
    }

    [Fact]
    public async Task class_throwing_not_implemented_in_block_body_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                void DoIt();
            }
            public class BadImpl : IService
            {
                public void DoIt()
                {
                    throw new System.NotImplementedException();
                }
            }
            """);
        Assert.Single(report.UnimplementedMembers);
    }

    [Fact]
    public async Task class_with_real_impl_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                void DoIt();
            }
            public class GoodImpl : IService
            {
                public void DoIt() { var x = 1 + 1; }
            }
            """);
        report.UnimplementedMembers.ShouldBeEmpty();
    }

    [Fact]
    public async Task class_with_throwing_stub_NOT_on_interface_is_not_flagged()
    {
        // The throw-stub method is not an interface implementation — must NOT be flagged
        var report = await AnalyzeSourceAsync("""
            public class StandaloneStub
            {
                public void Helper() => throw new System.NotImplementedException();
            }
            """);
        report.UnimplementedMembers.ShouldBeEmpty();
    }

    [Fact]
    public async Task throwing_property_getter_on_interface_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                int Value { get; }
            }
            public class BadImpl : IService
            {
                public int Value => throw new System.NotImplementedException();
            }
            """);
        Assert.Single(report.UnimplementedMembers);
        report.UnimplementedMembers[0].MemberName.ShouldBe("Value");
    }

    [Fact]
    public async Task unimplemented_members_are_sorted_by_type_then_member()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                void A();
                void B();
            }
            public class ZetaImpl : IService
            {
                public void A() => throw new System.NotImplementedException();
                public void B() => throw new System.NotImplementedException();
            }
            public class AlphaImpl : IService
            {
                public void A() => throw new System.NotImplementedException();
                public void B() => throw new System.NotImplementedException();
            }
            """);
        report.UnimplementedMembers.Count.ShouldBe(4);
        report.UnimplementedMembers[0].TypeName.ShouldBe("AlphaImpl");
        report.UnimplementedMembers[0].MemberName.ShouldBe("A");
        report.UnimplementedMembers[1].TypeName.ShouldBe("AlphaImpl");
        report.UnimplementedMembers[1].MemberName.ShouldBe("B");
        report.UnimplementedMembers[2].TypeName.ShouldBe("ZetaImpl");
        report.UnimplementedMembers[3].TypeName.ShouldBe("ZetaImpl");
    }

    [Fact]
    public async Task record_synthesized_equals_is_not_flagged_when_sibling_throws()
    {
        // Regression: record-synthesized Equals (from IEquatable<T>) has its
        // DeclaringSyntaxReferences pointing at the record declaration, which
        // would include sibling members that throw — those must not be attributed
        // to the synthesized member.
        var report = await AnalyzeSourceAsync("""
            public interface IRtOutput
            {
                void RenderText(System.IO.TextWriter writer);
            }
            public sealed record FakeResult : IRtOutput
            {
                public required string Name { get; init; }
                public void RenderText(System.IO.TextWriter writer)
                {
                    throw new System.NotSupportedException("Use the parent renderer");
                }
            }
            """);
        // Only RenderText should be flagged, not Equals or GetHashCode.
        Assert.Single(report.UnimplementedMembers);
        report.UnimplementedMembers[0].MemberName.ShouldBe("RenderText");
    }

    [Fact]
    public async Task abstract_class_throwing_on_interface_member_is_not_flagged()
    {
        // Abstract classes are typically excluded from "concrete implementation" checks
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                void DoIt();
            }
            public abstract class AbstractImpl : IService
            {
                public virtual void DoIt() => throw new System.NotImplementedException();
            }
            """);
        report.UnimplementedMembers.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Batch 2: Result types + SolidResult.ISP block
    // -----------------------------------------------------------------------

    [Fact]
    public void isp_fat_interface_item_has_required_fields()
    {
        var item = new IspFatInterfaceItem
        {
            InterfaceName = "IBig",
            FilePath = "src/IBig.cs",
            Line = 10,
            MemberCount = 12
        };
        item.InterfaceName.ShouldBe("IBig");
        item.FilePath.ShouldBe("src/IBig.cs");
        item.Line.ShouldBe(10);
        item.MemberCount.ShouldBe(12);
    }

    [Fact]
    public void isp_unimplemented_member_item_has_required_fields()
    {
        var item = new IspUnimplementedMemberItem
        {
            TypeName = "BadImpl",
            MemberName = "DoIt",
            InterfaceName = "IService",
            FilePath = "src/BadImpl.cs",
            Line = 20
        };
        item.TypeName.ShouldBe("BadImpl");
        item.MemberName.ShouldBe("DoIt");
        item.InterfaceName.ShouldBe("IService");
        item.FilePath.ShouldBe("src/BadImpl.cs");
        item.Line.ShouldBe(20);
    }

    [Fact]
    public void solid_result_isp_block_is_empty_by_default()
    {
        var result = new SolidResult();
        result.ISP.ShouldNotBeNull();
        result.ISP.FatInterfaces.ShouldBeEmpty();
        result.ISP.UnimplementedMembers.ShouldBeEmpty();
    }

    [Fact]
    public void solid_result_json_has_top_level_ISP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(StaticAnalyzerResultAdapters.Solid(result), output);

        using var doc = JsonDocument.Parse(output.ToString());
        doc.RootElement.TryGetProperty("ISP", out _).ShouldBeTrue();
    }

    [Fact]
    public void solid_result_json_isp_has_fatInterfaces_and_unimplementedMembers_arrays()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(StaticAnalyzerResultAdapters.Solid(result), output);

        using var doc = JsonDocument.Parse(output.ToString());
        var isp = doc.RootElement.GetProperty("ISP");
        isp.TryGetProperty("FatInterfaces", out var fat).ShouldBeTrue();
        isp.TryGetProperty("UnimplementedMembers", out var unimpl).ShouldBeTrue();
        fat.ValueKind.ShouldBe(JsonValueKind.Array);
        unimpl.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    // -----------------------------------------------------------------------
    // Batch 3: SolidCommand CLI wiring for ISP
    // -----------------------------------------------------------------------

    [Fact]
    public async Task execute_async_returns_1_for_negative_min_members()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "ISP", 10, 5, -1, 2);
        exitCode.ShouldBe(1);
    }

    [Fact]
    public void execute_async_defaults_min_members_to_7()
    {
        var cmd = App.Commands.Single(entry => entry.Info.Name == "solid").Build();
        var minMembersOpt = cmd.Options.OfType<Option<int>>()
            .First(o => o.Name == "min-members");
        minMembersOpt.Parse("").GetValueForOption(minMembersOpt).ShouldBe(7);
    }

    [Fact]
    public void principle_help_mentions_ISP()
    {
        var cmd = App.Commands.Single(entry => entry.Info.Name == "solid").Build();
        var principleOpt = cmd.Options.OfType<Option<string>>()
            .First(o => o.Name == "principle");
        Assert.Contains("ISP", principleOpt.Description, StringComparison.Ordinal);
    }
}
