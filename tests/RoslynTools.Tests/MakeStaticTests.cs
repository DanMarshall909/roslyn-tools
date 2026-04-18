using RoslynTools.Commands;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class MakeStaticTests
{
    // ──────────────────────────────────────────────────────────────────────────
    //  1. Clean method (no instance state) becomes static
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeStatic_CleanMethod_AddsStaticModifier()
    {
        using var proj = new TestProjectBuilder("StaticApp");
        proj.AddSource("Calc.cs", """
            class Calc {
                public int Add(int a, int b) { return a + b; }
            }
            """);
        var slnPath = await proj.BuildAsync();

        await MakeStaticCommand.ExecuteAsync("Calc.Add", slnPath, dryRun: false);

        var content = await proj.ReadSourceAsync("Calc.cs");
        Assert.Contains("public static int Add", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. `this.` usage blocks with clear message
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeStatic_ThisUsage_Rejected()
    {
        using var proj = new TestProjectBuilder("StaticApp");
        proj.AddSource("Service.cs", """
            class Service {
                private int _value = 0;
                public int GetValue() { return this._value; }
            }
            """);
        var slnPath = await proj.BuildAsync();

        var stderr = new System.IO.StringWriter();
        Console.SetError(stderr);

        await MakeStaticCommand.ExecuteAsync("Service.GetValue", slnPath, dryRun: false);

        var output = stderr.ToString();
        Assert.Contains("this", output);
        Assert.Equal(1, Environment.ExitCode);

        // Reset for other tests
        Environment.ExitCode = 0;
        Console.SetError(new System.IO.StreamWriter(System.IO.Stream.Null));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Instance-field usage blocks with clear message
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeStatic_InstanceFieldUsage_Rejected()
    {
        using var proj = new TestProjectBuilder("StaticApp");
        proj.AddSource("Repo.cs", """
            class Repo {
                private int _count;
                public int GetCount() { return _count; }
            }
            """);
        var slnPath = await proj.BuildAsync();

        var stderr = new System.IO.StringWriter();
        Console.SetError(stderr);

        await MakeStaticCommand.ExecuteAsync("Repo.GetCount", slnPath, dryRun: false);

        var output = stderr.ToString();
        Assert.Contains("instance member", output);
        Assert.Contains("_count", output);
        Assert.Equal(1, Environment.ExitCode);

        Environment.ExitCode = 0;
        Console.SetError(new System.IO.StreamWriter(System.IO.Stream.Null));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. virtual method blocks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeStatic_VirtualMethod_Rejected()
    {
        using var proj = new TestProjectBuilder("StaticApp");
        proj.AddSource("Base.cs", """
            class Base {
                public virtual string Describe() { return "base"; }
            }
            """);
        var slnPath = await proj.BuildAsync();

        var stderr = new System.IO.StringWriter();
        Console.SetError(stderr);

        await MakeStaticCommand.ExecuteAsync("Base.Describe", slnPath, dryRun: false);

        var output = stderr.ToString();
        Assert.Contains("virtual", output);
        Assert.Equal(1, Environment.ExitCode);

        Environment.ExitCode = 0;
        Console.SetError(new System.IO.StreamWriter(System.IO.Stream.Null));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Call sites updated across multiple files
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeStatic_CallSites_RewrittenToTypeName()
    {
        using var proj = new TestProjectBuilder("StaticApp");
        proj.AddSource("Helper.cs", """
            class Helper {
                public string Format(string s) { return s.Trim(); }
            }
            """);
        proj.AddSource("App.cs", """
            class App {
                void Run() {
                    var h = new Helper();
                    var result = h.Format("hello");
                }
            }
            """);
        var slnPath = await proj.BuildAsync();

        await MakeStaticCommand.ExecuteAsync("Helper.Format", slnPath, dryRun: false);

        var helperContent = await proj.ReadSourceAsync("Helper.cs");
        Assert.Contains("public static string Format", helperContent);

        var appContent = await proj.ReadSourceAsync("App.cs");
        Assert.Contains("Helper.Format(", appContent);
        Assert.DoesNotContain("h.Format(", appContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  6. Dry-run emits diff without writing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeStatic_DryRun_NoWrites()
    {
        using var proj = new TestProjectBuilder("StaticApp");
        proj.AddSource("Util.cs", """
            class Util {
                public int Square(int x) { return x * x; }
            }
            """);
        var slnPath = await proj.BuildAsync();

        var before = await proj.ReadSourceAsync("Util.cs");

        var sw = new System.IO.StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            await MakeStaticCommand.ExecuteAsync("Util.Square", slnPath, dryRun: true);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        var after = await proj.ReadSourceAsync("Util.cs");
        Assert.Equal(before, after);

        var diffOutput = sw.ToString();
        Assert.Contains("static", diffOutput);
    }
}
