using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

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
        Assert.Equal("Cls", item.TypeName);
        Assert.Equal("Describe", item.MemberName);
        Assert.Equal("switch", item.ViolationType);
        Assert.Equal(2, item.BranchCount);
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
        Assert.Empty(report.Violations);
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
        Assert.Empty(report.Violations);
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
        Assert.Equal(1, report.Violations[0].BranchCount);
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
        Assert.Equal("if-chain", item.ViolationType);
        Assert.Equal(2, item.BranchCount);
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
        Assert.Equal("if-chain", report.Violations[0].ViolationType);
        Assert.Equal(2, report.Violations[0].BranchCount);
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
        Assert.Empty(report.Violations);
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
        Assert.Empty(report.Violations);
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
        Assert.Empty(report.Violations);
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
        Assert.Equal(3, report.Violations.Count);
        Assert.Equal("Alpha", report.Violations[0].TypeName);
        Assert.Equal("Zeta", report.Violations[1].TypeName);
        Assert.Equal("A", report.Violations[1].MemberName);
        Assert.Equal("Zeta", report.Violations[2].TypeName);
        Assert.Equal("B", report.Violations[2].MemberName);
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
        Assert.Equal("Cls", item.TypeName);
        Assert.Equal("Describe", item.MemberName);
        Assert.Equal("switch", item.ViolationType);
        Assert.Equal(2, item.BranchCount);
    }

    [Fact]
    public void solid_result_ocp_list_is_empty_by_default()
    {
        var result = new SolidResult();
        Assert.Empty(result.OCP);
    }

    [Fact]
    public void solid_result_json_has_top_level_OCP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(result, output);

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.True(doc.RootElement.TryGetProperty("OCP", out var ocp));
        Assert.Equal(JsonValueKind.Array, ocp.ValueKind);
    }

    // -----------------------------------------------------------------------
    // Batch 3: SolidCommand CLI wiring for OCP
    // -----------------------------------------------------------------------

    [Fact]
    public async Task execute_async_returns_0_for_valid_ocp_principle()
    {
        var exitCode = await SolidCommand.ExecuteAsync(null, "OCP", 10, 5, 7, 2);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void execute_async_defaults_min_branches_to_2()
    {
        var cmd = SolidCommand.Build();
        var minBranchesOpt = cmd.Options.OfType<Option<int>>()
            .First(o => o.Name == "min-branches");
        Assert.Equal(2, minBranchesOpt.Parse("").GetValueForOption(minBranchesOpt));
    }

    [Fact]
    public void principle_help_mentions_OCP()
    {
        var cmd = SolidCommand.Build();
        var principleOpt = cmd.Options.OfType<Option<string>>()
            .First(o => o.Name == "principle");
        Assert.Contains("OCP", principleOpt.Description, StringComparison.Ordinal);
    }
}
