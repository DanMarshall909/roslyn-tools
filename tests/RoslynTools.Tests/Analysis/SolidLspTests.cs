using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class SolidLspTests
{
    // -----------------------------------------------------------------------
    // Batch 1: SolidAnalyzer.AnalyzeLspAsync heuristic engine
    // -----------------------------------------------------------------------

    private static async Task<SolidLspReport> AnalyzeSourceAsync(
        string source, string fileName = "Test.cs")
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource(fileName, source);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            return await SolidAnalyzer.AnalyzeLspAsync(sln);
        }
    }

    // ----- ThrowingOverrides -----

    [Fact]
    public async Task override_throwing_not_implemented_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Base
            {
                public virtual void DoIt() { }
            }
            public class Derived : Base
            {
                public override void DoIt() => throw new System.NotImplementedException();
            }
            """);
        Assert.Single(report.ThrowingOverrides);
        var item = report.ThrowingOverrides[0];
        item.TypeName.ShouldBe("Derived");
        item.MemberName.ShouldBe("DoIt");
        item.ExceptionType.ShouldBe("NotImplementedException");
    }

    [Fact]
    public async Task override_throwing_not_supported_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Base
            {
                public virtual void DoIt() { }
            }
            public class Derived : Base
            {
                public override void DoIt() => throw new System.NotSupportedException();
            }
            """);
        Assert.Single(report.ThrowingOverrides);
        report.ThrowingOverrides[0].ExceptionType.ShouldBe("NotSupportedException");
    }

    [Fact]
    public async Task override_with_real_impl_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Base
            {
                public virtual void DoIt() { }
            }
            public class Derived : Base
            {
                public override void DoIt() { var x = 1; }
            }
            """);
        report.ThrowingOverrides.ShouldBeEmpty();
    }

    [Fact]
    public async Task non_override_method_throwing_is_not_flagged()
    {
        // Method that throws but is not an override — not an LSP violation
        var report = await AnalyzeSourceAsync("""
            public class Standalone
            {
                public void DoIt() => throw new System.NotImplementedException();
            }
            """);
        report.ThrowingOverrides.ShouldBeEmpty();
    }

    [Fact]
    public async Task override_with_block_body_throw_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Base
            {
                public virtual void DoIt() { }
            }
            public class Derived : Base
            {
                public override void DoIt()
                {
                    throw new System.NotImplementedException();
                }
            }
            """);
        Assert.Single(report.ThrowingOverrides);
    }

    // ----- ContractNarrowings (explicit interface impls that throw on every path) -----

    [Fact]
    public async Task explicit_interface_impl_throwing_is_flagged_as_contract_narrowing()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                void DoIt();
            }
            public class BadImpl : IService
            {
                void IService.DoIt() => throw new System.NotImplementedException();
            }
            """);
        Assert.Single(report.ContractNarrowings);
        var item = report.ContractNarrowings[0];
        item.TypeName.ShouldBe("BadImpl");
        item.MemberName.ShouldBe("DoIt");
        item.BaseType.ShouldBe("IService");
    }

    [Fact]
    public async Task explicit_interface_impl_with_real_body_is_not_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                void DoIt();
            }
            public class GoodImpl : IService
            {
                void IService.DoIt() { var x = 1; }
            }
            """);
        report.ContractNarrowings.ShouldBeEmpty();
    }

    [Fact]
    public async Task implicit_interface_impl_throwing_is_not_flagged_under_lsp()
    {
        // Implicit interface impls are ISP territory, not LSP — keep LSP narrow
        var report = await AnalyzeSourceAsync("""
            public interface IService
            {
                void DoIt();
            }
            public class BadImpl : IService
            {
                public void DoIt() => throw new System.NotImplementedException();
            }
            """);
        report.ContractNarrowings.ShouldBeEmpty();
    }

    [Fact]
    public async Task results_are_sorted_by_type_then_member()
    {
        var report = await AnalyzeSourceAsync("""
            public class Base
            {
                public virtual void A() { }
                public virtual void B() { }
            }
            public class Zeta : Base
            {
                public override void B() => throw new System.NotImplementedException();
                public override void A() => throw new System.NotImplementedException();
            }
            public class Alpha : Base
            {
                public override void A() => throw new System.NotImplementedException();
            }
            """);
        report.ThrowingOverrides.Count.ShouldBe(3);
        report.ThrowingOverrides[0].TypeName.ShouldBe("Alpha");
        report.ThrowingOverrides[1].TypeName.ShouldBe("Zeta");
        report.ThrowingOverrides[1].MemberName.ShouldBe("A");
        report.ThrowingOverrides[2].TypeName.ShouldBe("Zeta");
        report.ThrowingOverrides[2].MemberName.ShouldBe("B");
    }

    [Fact]
    public async Task record_synthesized_overrides_are_not_flagged_when_sibling_throws()
    {
        // Regression: record-synthesized Equals/GetHashCode/ToString have their
        // DeclaringSyntaxReferences pointing at the record declaration, which would
        // include sibling members that throw — those must not be attributed to the
        // synthesized override.
        var report = await AnalyzeSourceAsync("""
            public sealed record FakeResult
            {
                public required string Name { get; init; }
                public void ThrowingHelper() => throw new System.NotSupportedException("nope");
            }
            """);
        report.ThrowingOverrides.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Batch 2: Result types + SolidResult.LSP block
    // -----------------------------------------------------------------------

    [Fact]
    public void lsp_throwing_override_item_has_required_fields()
    {
        var item = new LspThrowingOverrideItem
        {
            TypeName = "Derived",
            MemberName = "DoIt",
            ExceptionType = "NotImplementedException",
            FilePath = "src/Derived.cs",
            Line = 10
        };
        item.TypeName.ShouldBe("Derived");
        item.MemberName.ShouldBe("DoIt");
        item.ExceptionType.ShouldBe("NotImplementedException");
    }

    [Fact]
    public void lsp_contract_narrowing_item_has_required_fields()
    {
        var item = new LspContractNarrowingItem
        {
            TypeName = "BadImpl",
            MemberName = "DoIt",
            BaseType = "IService",
            FilePath = "src/BadImpl.cs",
            Line = 20
        };
        item.TypeName.ShouldBe("BadImpl");
        item.BaseType.ShouldBe("IService");
    }

    [Fact]
    public void solid_result_lsp_block_is_empty_by_default()
    {
        var result = new SolidResult();
        result.LSP.ShouldNotBeNull();
        result.LSP.ThrowingOverrides.ShouldBeEmpty();
        result.LSP.ContractNarrowings.ShouldBeEmpty();
    }

    [Fact]
    public void solid_result_json_has_top_level_LSP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(result, output);

        using var doc = JsonDocument.Parse(output.ToString());
        doc.RootElement.TryGetProperty("LSP", out var lsp).ShouldBeTrue();
        lsp.TryGetProperty("ThrowingOverrides", out var to).ShouldBeTrue();
        lsp.TryGetProperty("ContractNarrowings", out var cn).ShouldBeTrue();
        to.ValueKind.ShouldBe(JsonValueKind.Array);
        cn.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    // -----------------------------------------------------------------------
    // Batch 3: SolidCommand CLI wiring for LSP
    // -----------------------------------------------------------------------

    [Fact]
    public async Task execute_async_returns_0_for_valid_lsp_principle()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "LSP", 10, 5, 7, 2);
        exitCode.ShouldBe(0);
    }

    [Fact]
    public void principle_help_mentions_LSP()
    {
        var cmd = SolidCommand.Build();
        var principleOpt = cmd.Options.OfType<Option<string>>()
            .First(o => o.Name == "principle");
        Assert.Contains("LSP", principleOpt.Description, StringComparison.Ordinal);
    }
}
