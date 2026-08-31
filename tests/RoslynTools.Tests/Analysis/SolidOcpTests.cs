using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Services;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class SolidOcpTests
{
    // -----------------------------------------------------------------------
    // Batch 1: SolidAnalyzer.AnalyzeOcpAsync heuristic engine
    // -----------------------------------------------------------------------

    private static async Task<SolidOcpReport> AnalyzeSourceAsync(
        string source, int minBranches = 2, string fileName = "Test.cs")
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource(fileName, source);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            return await SolidAnalyzer.AnalyzeOcpAsync(sln, minBranches);
        }
    }

    [Fact]
    public async Task switch_on_is_concrete_type_with_two_arms_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Cat { }
            public class Dog { }
            public class Cls
            {
                public string Describe(object o) => o switch
                {
                    Cat c => "cat",
                    Dog d => "dog",
                    _ => "unknown"
                };
            }
            """);
        Assert.Single(report.Violations);
        var item = report.Violations[0];
        item.TypeName.ShouldBe("Cls");
        item.MemberName.ShouldBe("Describe");
        item.ViolationType.ShouldBe("switch");
        item.BranchCount.ShouldBe(2);
    }

    [Fact]
    public async Task switch_on_enum_value_is_not_flagged()
    {
        // Switching on a value (enum) is fine — not OCP
        var report = await AnalyzeSourceAsync("""
            public enum Mood { Happy, Sad }
            public class Cls
            {
                public string Describe(Mood m) => m switch
                {
                    Mood.Happy => "happy",
                    Mood.Sad => "sad",
                    _ => "neutral"
                };
            }
            """);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task switch_on_is_concrete_below_threshold_is_not_flagged()
    {
        // Only one type arm — below default threshold of 2
        var report = await AnalyzeSourceAsync("""
            public class Cat { }
            public class Cls
            {
                public string Describe(object o) => o switch
                {
                    Cat c => "cat",
                    _ => "unknown"
                };
            }
            """);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task switch_threshold_is_configurable()
    {
        var report = await AnalyzeSourceAsync("""
            public class Cat { }
            public class Cls
            {
                public string Describe(object o) => o switch
                {
                    Cat c => "cat",
                    _ => "unknown"
                };
            }
            """, minBranches: 1);
        Assert.Single(report.Violations);
        report.Violations[0].BranchCount.ShouldBe(1);
    }

    [Fact]
    public async Task if_chain_with_is_concrete_branches_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Cat { }
            public class Dog { }
            public class Cls
            {
                public string Describe(object o)
                {
                    if (o is Cat) return "cat";
                    else if (o is Dog) return "dog";
                    else return "unknown";
                }
            }
            """);
        Assert.Single(report.Violations);
        var item = report.Violations[0];
        item.ViolationType.ShouldBe("if-chain");
        item.BranchCount.ShouldBe(2);
    }

    [Fact]
    public async Task if_chain_with_get_type_typeof_comparisons_is_flagged()
    {
        var report = await AnalyzeSourceAsync("""
            public class Cat { }
            public class Dog { }
            public class Cls
            {
                public string Describe(object o)
                {
                    if (o.GetType() == typeof(Cat)) return "cat";
                    else if (o.GetType() == typeof(Dog)) return "dog";
                    return "unknown";
                }
            }
            """);
        Assert.Single(report.Violations);
        report.Violations[0].ViolationType.ShouldBe("if-chain");
        report.Violations[0].BranchCount.ShouldBe(2);
    }

    [Fact]
    public async Task single_if_is_check_not_flagged()
    {
        // Single 'is' check is a fine type guard — only flag chains
        var report = await AnalyzeSourceAsync("""
            public class Cat { }
            public class Cls
            {
                public void DoIt(object o)
                {
                    if (o is Cat) return;
                }
            }
            """);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task if_chain_with_value_comparisons_is_not_flagged()
    {
        // Plain value comparisons are not type-discriminating
        var report = await AnalyzeSourceAsync("""
            public class Cls
            {
                public string Describe(int x)
                {
                    if (x == 1) return "one";
                    else if (x == 2) return "two";
                    return "other";
                }
            }
            """);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task switch_on_interface_pattern_is_not_flagged()
    {
        // Switching on interface types is open to extension — not an OCP violation
        var report = await AnalyzeSourceAsync("""
            public interface IPet { }
            public interface IWild { }
            public class Cls
            {
                public string Describe(object o) => o switch
                {
                    IPet p => "pet",
                    IWild w => "wild",
                    _ => "unknown"
                };
            }
            """);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task violations_are_sorted_by_type_then_member()
    {
        var report = await AnalyzeSourceAsync("""
            public class Cat { }
            public class Dog { }
            public class Zeta
            {
                public string B(object o) => o switch { Cat => "c", Dog => "d", _ => "?" };
                public string A(object o) => o switch { Cat => "c", Dog => "d", _ => "?" };
            }
            public class Alpha
            {
                public string X(object o) => o switch { Cat => "c", Dog => "d", _ => "?" };
            }
            """);
        report.Violations.Count.ShouldBe(3);
        report.Violations[0].TypeName.ShouldBe("Alpha");
        report.Violations[1].TypeName.ShouldBe("Zeta");
        report.Violations[1].MemberName.ShouldBe("A");
        report.Violations[2].TypeName.ShouldBe("Zeta");
        report.Violations[2].MemberName.ShouldBe("B");
    }

    // -----------------------------------------------------------------------
    // Batch 2: Result types + SolidResult.OCP block
    // -----------------------------------------------------------------------

    [Fact]
    public void ocp_violation_item_has_required_fields()
    {
        var item = new OcpViolationItem
        {
            TypeName = "Cls",
            MemberName = "Describe",
            FilePath = "src/Cls.cs",
            Line = 10,
            BranchCount = 2,
            ViolationType = "switch"
        };
        item.TypeName.ShouldBe("Cls");
        item.MemberName.ShouldBe("Describe");
        item.ViolationType.ShouldBe("switch");
        item.BranchCount.ShouldBe(2);
    }

    [Fact]
    public void solid_result_ocp_list_is_empty_by_default()
    {
        var result = new SolidResult();
        result.OCP.ShouldBeEmpty();
    }

    [Fact]
    public void solid_result_json_has_top_level_OCP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(StaticAnalyzerResultAdapters.Solid(result), output);

        using var doc = JsonDocument.Parse(output.ToString());
        doc.RootElement.TryGetProperty("OCP", out var ocp).ShouldBeTrue();
        ocp.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    // -----------------------------------------------------------------------
    // Batch 3: SolidCommand CLI wiring for OCP
    // -----------------------------------------------------------------------

    [Fact]
    public async Task execute_async_returns_0_for_valid_ocp_principle()
    {
        using var trust = RepositoryTrustContext.UseExplicitTrust();
        var exitCode = await SolidCommand.ExecuteAsync(null, "OCP", 10, 5, 7, 2);
        exitCode.ShouldBe(0);
    }

    [Fact]
    public void execute_async_defaults_min_branches_to_2()
    {
        var cmd = App.Commands.Single(entry => entry.Info.Name == "solid").Build();
        var minBranchesOpt = cmd.Options.OfType<Option<int>>()
            .First(o => o.Name == "min-branches");
        minBranchesOpt.Parse("").GetValueForOption(minBranchesOpt).ShouldBe(2);
    }

    [Fact]
    public void principle_help_mentions_OCP()
    {
        var cmd = App.Commands.Single(entry => entry.Info.Name == "solid").Build();
        var principleOpt = cmd.Options.OfType<Option<string>>()
            .First(o => o.Name == "principle");
        Assert.Contains("OCP", principleOpt.Description, StringComparison.Ordinal);
    }
}
