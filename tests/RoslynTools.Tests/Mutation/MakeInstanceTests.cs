using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class MakeInstanceTests
{
    // ── 1. Call site with reachable local variable ────────────────────────────

    [Fact]
    public async Task Rewrites_call_sites_to_use_the_local_instance()
    {
        const string source = """
            namespace App;

            public class Calculator
            {
                public static int Add(int a, int b) => a + b;
            }

            public class Consumer
            {
                public void Run()
                {
                    var calc = new Calculator();
                    var result = Calculator.Add(1, 2);
                }
            }
            """;

        using var proj = new AdhocProjectBuilder("App");
        proj.AddSource("Calc.cs", source);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeInstanceCommand.ExecuteAsync("Calculator.Add", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Calc.cs");
        // Static modifier removed
        Assert.DoesNotContain("public static int Add", content);
        Assert.Contains("public int Add", content);
        // Call site rewritten to use local var
        Assert.Contains("calc.Add(1, 2)", content);
        Assert.DoesNotContain("Calculator.Add(", content);
    }

    // ── 2. Call site with reachable field ────────────────────────────────────

    [Fact]
    public async Task Rewrites_call_sites_to_use_the_field_instance()
    {
        const string source = """
            namespace App;

            public class Formatter
            {
                public static string Format(string value) => value.Trim();
            }

            public class Report
            {
                private readonly Formatter _formatter = new Formatter();

                public string Build(string raw)
                {
                    return Formatter.Format(raw);
                }
            }
            """;

        using var proj = new AdhocProjectBuilder("App");
        proj.AddSource("Fmt.cs", source);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeInstanceCommand.ExecuteAsync("Formatter.Format", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Fmt.cs");
        Assert.DoesNotContain("public static string Format", content);
        Assert.Contains("public string Format", content);
        Assert.Contains("_formatter.Format(raw)", content);
        Assert.DoesNotContain("Formatter.Format(", content);
    }

    // ── 3. Call site with no reachable instance — warn and skip ─────────────

    [Fact]
    public async Task Updates_the_declaration_but_leaves_unreachable_static_call_sites_unchanged()
    {
        const string source = """
            namespace App;

            public class Parser
            {
                public static int Parse(string s) => int.Parse(s);
            }

            public class StaticCaller
            {
                public static int DoWork(string input)
                {
                    return Parser.Parse(input);
                }
            }
            """;

        using var proj = new AdhocProjectBuilder("App");
        proj.AddSource("Parser.cs", source);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Parser.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeInstanceCommand.ExecuteAsync("Parser.Parse", "ignored", dryRun: false);

        var after = await proj.ReadSourceAsync("Parser.cs");

        // Declaration must be updated (static removed)
        Assert.DoesNotContain("public static int Parse", after);
        Assert.Contains("public int Parse", after);

        // Call site must remain unchanged (no instance reachable from a static method)
        Assert.Contains("Parser.Parse(input)", after);
    }

    // ── 4. Same-class call site — rewrite to this.Method(...) ────────────────

    [Fact]
    public async Task Rewrites_call_sites_to_use_this()
    {
        const string source = """
            namespace App;

            public class Service
            {
                public static string Sanitize(string s) => s.Trim();

                public string Process(string input)
                {
                    return Service.Sanitize(input);
                }
            }
            """;

        using var proj = new AdhocProjectBuilder("App");
        proj.AddSource("Service.cs", source);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeInstanceCommand.ExecuteAsync("Service.Sanitize", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Service.cs");
        Assert.DoesNotContain("public static string Sanitize", content);
        Assert.Contains("public string Sanitize", content);
        Assert.Contains("this.Sanitize(input)", content);
        Assert.DoesNotContain("Service.Sanitize(", content);
    }

    // ── 5. Same-class static call site — no instance context, warn + skip ────

    [Fact]
    public async Task Updates_the_declaration_but_leaves_same_class_static_call_sites_unchanged()
    {
        const string source = """
            namespace App;

            public class Helper
            {
                public static int Double(int x) => x * 2;

                public static int QuadViaStatic(int x)
                {
                    return Helper.Double(Helper.Double(x));
                }
            }
            """;

        using var proj = new AdhocProjectBuilder("App");
        proj.AddSource("Helper.cs", source);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeInstanceCommand.ExecuteAsync("Helper.Double", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Helper.cs");
        // Declaration updated
        Assert.DoesNotContain("public static int Double", content);
        Assert.Contains("public int Double", content);
        // Static call sites left unchanged (no instance reachable)
        Assert.Contains("Helper.Double(", content);
    }

    // ── 6. Dry-run — no writes ────────────────────────────────────────────────

    [Fact]
    public async Task Does_not_modify_files()
    {
        const string source = """
            namespace App;

            public class Greeter
            {
                public static string Hello(string name) => $"Hello {name}";
            }

            public class App
            {
                public void Run()
                {
                    var g = new Greeter();
                    _ = Greeter.Hello("world");
                }
            }
            """;

        using var proj = new AdhocProjectBuilder("App");
        proj.AddSource("Greeter.cs", source);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Greeter.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeInstanceCommand.ExecuteAsync("Greeter.Hello", "ignored", dryRun: true);

        var after = await proj.ReadSourceAsync("Greeter.cs");
        Assert.Equal(before, after);
    }

    // ── 7. Multi-file: call sites in a different file rewritten ──────────────

    [Fact]
    public async Task Rewrites_call_sites_in_other_files()
    {
        const string helperSource = """
            namespace App;

            public class MathUtil
            {
                public static int Square(int x) => x * x;
            }
            """;

        const string callerSource = """
            namespace App;

            public class Processor
            {
                private readonly MathUtil _util = new MathUtil();

                public int Process(int value)
                {
                    return MathUtil.Square(value);
                }
            }
            """;

        using var proj = new AdhocProjectBuilder("App");
        proj.AddSource("MathUtil.cs", helperSource);
        proj.AddSource("Processor.cs", callerSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeInstanceCommand.ExecuteAsync("MathUtil.Square", "ignored", dryRun: false);

        var utilContent = await proj.ReadSourceAsync("MathUtil.cs");
        Assert.DoesNotContain("public static int Square", utilContent);
        Assert.Contains("public int Square", utilContent);

        var callerContent = await proj.ReadSourceAsync("Processor.cs");
        Assert.Contains("_util.Square(value)", callerContent);
        Assert.DoesNotContain("MathUtil.Square(", callerContent);
    }

    [Fact]
    public async Task Warns_for_ambiguous_reachability_and_still_rewrites_later_call_sites_in_the_same_document()
    {
        const string source = """
            namespace App;

            public class Utility
            {
                public static void Ping() { }
            }

            public class Consumer
            {
                private readonly Utility _first = new();
                private readonly Utility _second = new();

                public void Run()
                {
                    Utility.Ping();

                    var utility = new Utility();
                    Utility.Ping();
                }
            }
            """;

        using var proj = new AdhocProjectBuilder("App");
        proj.AddSource("Utility.cs", source);
        var (workspace, sln) = proj.Build();

        var previousError = Console.Error;
        using var errorCapture = new StringWriter();
        Console.SetError(errorCapture);
        try
        {
            using (WorkspaceLoader.UseOverride(workspace, sln))
                await MakeInstanceCommand.ExecuteAsync("Utility.Ping", "ignored", dryRun: false);
        }
        finally
        {
            Console.SetError(previousError);
        }

        var content = await proj.ReadSourceAsync("Utility.cs");
        Assert.Contains("Utility.Ping();", content);
        Assert.Contains("utility.Ping();", content);

        var stderr = errorCapture.ToString();
        Assert.Contains("ambiguous reachable instance", stderr);
    }
}
