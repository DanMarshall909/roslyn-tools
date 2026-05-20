using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

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
        Assert.Equal("Derived", item.TypeName);
        Assert.Equal("DoIt", item.MemberName);
        Assert.Equal("NotImplementedException", item.ExceptionType);
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
        Assert.Equal("NotSupportedException", report.ThrowingOverrides[0].ExceptionType);
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
        Assert.Empty(report.ThrowingOverrides);
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
        Assert.Empty(report.ThrowingOverrides);
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
        Assert.Equal("BadImpl", item.TypeName);
        Assert.Equal("DoIt", item.MemberName);
        Assert.Equal("IService", item.BaseType);
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
        Assert.Empty(report.ContractNarrowings);
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
        Assert.Empty(report.ContractNarrowings);
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
        Assert.Equal(3, report.ThrowingOverrides.Count);
        Assert.Equal("Alpha", report.ThrowingOverrides[0].TypeName);
        Assert.Equal("Zeta", report.ThrowingOverrides[1].TypeName);
        Assert.Equal("A", report.ThrowingOverrides[1].MemberName);
        Assert.Equal("Zeta", report.ThrowingOverrides[2].TypeName);
        Assert.Equal("B", report.ThrowingOverrides[2].MemberName);
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
        Assert.Empty(report.ThrowingOverrides);
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
        Assert.Equal("Derived", item.TypeName);
        Assert.Equal("DoIt", item.MemberName);
        Assert.Equal("NotImplementedException", item.ExceptionType);
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
        Assert.Equal("BadImpl", item.TypeName);
        Assert.Equal("IService", item.BaseType);
    }

    [Fact]
    public void solid_result_lsp_block_is_empty_by_default()
    {
        var result = new SolidResult();
        Assert.NotNull(result.LSP);
        Assert.Empty(result.LSP.ThrowingOverrides);
        Assert.Empty(result.LSP.ContractNarrowings);
    }

    [Fact]
    public void solid_result_json_has_top_level_LSP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(result, output);

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.True(doc.RootElement.TryGetProperty("LSP", out var lsp));
        Assert.True(lsp.TryGetProperty("ThrowingOverrides", out var to));
        Assert.True(lsp.TryGetProperty("ContractNarrowings", out var cn));
        Assert.Equal(JsonValueKind.Array, to.ValueKind);
        Assert.Equal(JsonValueKind.Array, cn.ValueKind);
    }

    // -----------------------------------------------------------------------
    // Batch 3: SolidCommand CLI wiring for LSP
    // -----------------------------------------------------------------------

    [Fact]
    public async Task execute_async_returns_0_for_valid_lsp_principle()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "LSP", 10, 5, 7, 2);
        Assert.Equal(0, exitCode);
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
