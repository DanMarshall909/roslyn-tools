using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

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
        Assert.Empty(report.FatInterfaces);
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
        Assert.Equal("IBig", report.FatInterfaces[0].InterfaceName);
        Assert.Equal(7, report.FatInterfaces[0].MemberCount);
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
        Assert.Equal(7, report.FatInterfaces[0].MemberCount);
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
        Assert.Empty(report.FatInterfaces);

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
        Assert.Equal("IBase", report2.FatInterfaces[0].InterfaceName);
        Assert.Equal(6, report2.FatInterfaces[0].MemberCount);
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
        Assert.EndsWith("Foo.cs", report.FatInterfaces[0].FilePath);
        Assert.Equal(1, report.FatInterfaces[0].Line);
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
        Assert.Equal(2, report.FatInterfaces.Count);
        Assert.Equal("IApple", report.FatInterfaces[0].InterfaceName);
        Assert.Equal("IZebra", report.FatInterfaces[1].InterfaceName);
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
        Assert.Equal("BadImpl", item.TypeName);
        Assert.Equal("DoIt", item.MemberName);
        Assert.Equal("IService", item.InterfaceName);
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
        Assert.Equal("DoIt", report.UnimplementedMembers[0].MemberName);
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
        Assert.Empty(report.UnimplementedMembers);
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
        Assert.Empty(report.UnimplementedMembers);
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
        Assert.Equal("Value", report.UnimplementedMembers[0].MemberName);
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
        Assert.Equal(4, report.UnimplementedMembers.Count);
        Assert.Equal("AlphaImpl", report.UnimplementedMembers[0].TypeName);
        Assert.Equal("A", report.UnimplementedMembers[0].MemberName);
        Assert.Equal("AlphaImpl", report.UnimplementedMembers[1].TypeName);
        Assert.Equal("B", report.UnimplementedMembers[1].MemberName);
        Assert.Equal("ZetaImpl", report.UnimplementedMembers[2].TypeName);
        Assert.Equal("ZetaImpl", report.UnimplementedMembers[3].TypeName);
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
        Assert.Empty(report.UnimplementedMembers);
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
        Assert.Equal("IBig", item.InterfaceName);
        Assert.Equal("src/IBig.cs", item.FilePath);
        Assert.Equal(10, item.Line);
        Assert.Equal(12, item.MemberCount);
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
        Assert.Equal("BadImpl", item.TypeName);
        Assert.Equal("DoIt", item.MemberName);
        Assert.Equal("IService", item.InterfaceName);
        Assert.Equal("src/BadImpl.cs", item.FilePath);
        Assert.Equal(20, item.Line);
    }

    [Fact]
    public void solid_result_isp_block_is_empty_by_default()
    {
        var result = new SolidResult();
        Assert.NotNull(result.ISP);
        Assert.Empty(result.ISP.FatInterfaces);
        Assert.Empty(result.ISP.UnimplementedMembers);
    }

    [Fact]
    public void solid_result_json_has_top_level_ISP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(result, output);

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.True(doc.RootElement.TryGetProperty("ISP", out _));
    }

    [Fact]
    public void solid_result_json_isp_has_fatInterfaces_and_unimplementedMembers_arrays()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(result, output);

        using var doc = JsonDocument.Parse(output.ToString());
        var isp = doc.RootElement.GetProperty("ISP");
        Assert.True(isp.TryGetProperty("FatInterfaces", out var fat));
        Assert.True(isp.TryGetProperty("UnimplementedMembers", out var unimpl));
        Assert.Equal(JsonValueKind.Array, fat.ValueKind);
        Assert.Equal(JsonValueKind.Array, unimpl.ValueKind);
    }

    // -----------------------------------------------------------------------
    // Batch 3: SolidCommand CLI wiring for ISP
    // -----------------------------------------------------------------------

    [Fact]
    public async Task execute_async_returns_0_for_valid_isp_principle()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "ISP", 10, 5, 7);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task execute_async_returns_1_for_negative_min_members()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "ISP", 10, 5, -1);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void execute_async_defaults_min_members_to_7()
    {
        var cmd = SolidCommand.Build();
        var minMembersOpt = cmd.Options.OfType<Option<int>>()
            .First(o => o.Name == "min-members");
        Assert.Equal(7, minMembersOpt.Parse("").GetValueForOption(minMembersOpt));
    }

    [Fact]
    public void principle_help_mentions_ISP()
    {
        var cmd = SolidCommand.Build();
        var principleOpt = cmd.Options.OfType<Option<string>>()
            .First(o => o.Name == "principle");
        Assert.Contains("ISP", principleOpt.Description, StringComparison.Ordinal);
    }
}
