using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class MakeInstance : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public MakeInstance(RtServicesFixture services) => _services = services;

    // ── 1. Call site with reachable local variable ────────────────────────────

    [Fact]
    public async Task rewrites_call_sites_to_use_the_local_instance()
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
        {
            await _services.Resolve<MakeInstanceCommand>().ExecuteAsync("Calculator.Add", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Calc.cs");
        // Static modifier removed
        content.ShouldNotContain("public static int Add", Case.Sensitive);
        content.ShouldContain("public int Add", Case.Sensitive);
        // Call site rewritten to use local var
        content.ShouldContain("calc.Add(1, 2)", Case.Sensitive);
        content.ShouldNotContain("Calculator.Add(", Case.Sensitive);
    }

    // ── 2. Call site with reachable field ────────────────────────────────────

    [Fact]
    public async Task rewrites_call_sites_to_use_the_field_instance()
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
        {
            await _services.Resolve<MakeInstanceCommand>().ExecuteAsync("Formatter.Format", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Fmt.cs");
        content.ShouldNotContain("public static string Format", Case.Sensitive);
        content.ShouldContain("public string Format", Case.Sensitive);
        content.ShouldContain("_formatter.Format(raw)", Case.Sensitive);
        content.ShouldNotContain("Formatter.Format(", Case.Sensitive);
    }

    // ── 3. Call site with no reachable instance — warn and skip ─────────────

    [Fact]
    public async Task updates_the_declaration_but_leaves_unreachable_static_call_sites_unchanged()
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
        {
            await _services.Resolve<MakeInstanceCommand>().ExecuteAsync("Parser.Parse", "ignored", false);
        }

        var after = await proj.ReadSourceAsync("Parser.cs");

        // Declaration must be updated (static removed)
        after.ShouldNotContain("public static int Parse", Case.Sensitive);
        after.ShouldContain("public int Parse", Case.Sensitive);

        // Call site must remain unchanged (no instance reachable from a static method)
        after.ShouldContain("Parser.Parse(input)", Case.Sensitive);
    }

    // ── 4. Same-class call site — rewrite to this.Method(...) ────────────────

    [Fact]
    public async Task rewrites_call_sites_to_use_this()
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
        {
            await _services.Resolve<MakeInstanceCommand>().ExecuteAsync("Service.Sanitize", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Service.cs");
        content.ShouldNotContain("public static string Sanitize", Case.Sensitive);
        content.ShouldContain("public string Sanitize", Case.Sensitive);
        content.ShouldContain("this.Sanitize(input)", Case.Sensitive);
        content.ShouldNotContain("Service.Sanitize(", Case.Sensitive);
    }

    // ── 5. Same-class static call site — no instance context, warn + skip ────

    [Fact]
    public async Task updates_the_declaration_but_leaves_same_class_static_call_sites_unchanged()
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
        {
            await _services.Resolve<MakeInstanceCommand>().ExecuteAsync("Helper.Double", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Helper.cs");
        // Declaration updated
        content.ShouldNotContain("public static int Double", Case.Sensitive);
        content.ShouldContain("public int Double", Case.Sensitive);
        // Static call sites left unchanged (no instance reachable)
        content.ShouldContain("Helper.Double(", Case.Sensitive);
    }

    // ── 6. Dry-run — no writes ────────────────────────────────────────────────

    [Fact]
    public async Task does_not_modify_files()
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
        {
            await _services.Resolve<MakeInstanceCommand>().ExecuteAsync("Greeter.Hello", "ignored", true);
        }

        var after = await proj.ReadSourceAsync("Greeter.cs");
        after.ShouldBe(before);
    }

    // ── 7. Multi-file: call sites in a different file rewritten ──────────────

    [Fact]
    public async Task rewrites_call_sites_in_other_files()
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
        {
            await _services.Resolve<MakeInstanceCommand>().ExecuteAsync("MathUtil.Square", "ignored", false);
        }

        var utilContent = await proj.ReadSourceAsync("MathUtil.cs");
        utilContent.ShouldNotContain("public static int Square", Case.Sensitive);
        utilContent.ShouldContain("public int Square", Case.Sensitive);

        var callerContent = await proj.ReadSourceAsync("Processor.cs");
        callerContent.ShouldContain("_util.Square(value)", Case.Sensitive);
        callerContent.ShouldNotContain("MathUtil.Square(", Case.Sensitive);
    }

    [Fact]
    public async Task warns_for_ambiguous_reachability_and_still_rewrites_later_call_sites_in_the_same_document()
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
            {
                await _services.Resolve<MakeInstanceCommand>().ExecuteAsync("Utility.Ping", "ignored", false);
            }
        }
        finally
        {
            Console.SetError(previousError);
        }

        var content = await proj.ReadSourceAsync("Utility.cs");
        content.ShouldContain("Utility.Ping();", Case.Sensitive);
        content.ShouldContain("utility.Ping();", Case.Sensitive);

        var stderr = errorCapture.ToString();
        stderr.ShouldContain("ambiguous reachable instance", Case.Sensitive);
    }
}
