using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class MakeStatic
{
    // ──────────────────────────────────────────────────────────────────────────
    //  1. Clean method (no instance state) becomes static
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task adds_the_static_modifier()
    {
        using var proj = new AdhocProjectBuilder("StaticApp");
        proj.AddSource("Calc.cs", """
                                  class Calc {
                                      public int Add(int a, int b) { return a + b; }
                                  }
                                  """);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await MakeStaticCommand.ExecuteAsync("Calc.Add", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Calc.cs");
        Assert.Contains("public static int Add", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. `this.` usage blocks with clear message
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task refuses_the_conversion_when_the_method_uses_this()
    {
        using var proj = new AdhocProjectBuilder("StaticApp");
        proj.AddSource("Service.cs", """
                                     class Service {
                                         private int _value = 0;
                                         public int GetValue() { return this._value; }
                                     }
                                     """);
        var (workspace, sln) = proj.Build();

        var stderr = new StringWriter();
        Console.SetError(stderr);

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await MakeStaticCommand.ExecuteAsync("Service.GetValue", "ignored", false);
        }

        var output = stderr.ToString();
        Assert.Contains("this", output);
        Assert.Equal(1, Environment.ExitCode);

        // Reset for other tests
        Environment.ExitCode = 0;
        Console.SetError(new StreamWriter(Stream.Null));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Instance-field usage blocks with clear message
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task refuses_the_conversion_when_the_method_uses_instance_fields()
    {
        using var proj = new AdhocProjectBuilder("StaticApp");
        proj.AddSource("Repo.cs", """
                                  class Repo {
                                      private int _count;
                                      public int GetCount() { return _count; }
                                  }
                                  """);
        var (workspace, sln) = proj.Build();

        var stderr = new StringWriter();
        Console.SetError(stderr);

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await MakeStaticCommand.ExecuteAsync("Repo.GetCount", "ignored", false);
        }

        var output = stderr.ToString();
        Assert.Contains("instance member", output);
        Assert.Contains("_count", output);
        Assert.Equal(1, Environment.ExitCode);

        Environment.ExitCode = 0;
        Console.SetError(new StreamWriter(Stream.Null));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. virtual method blocks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task refuses_the_conversion_for_virtual_methods()
    {
        using var proj = new AdhocProjectBuilder("StaticApp");
        proj.AddSource("Base.cs", """
                                  class Base {
                                      public virtual string Describe() { return "base"; }
                                  }
                                  """);
        var (workspace, sln) = proj.Build();

        var stderr = new StringWriter();
        Console.SetError(stderr);

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await MakeStaticCommand.ExecuteAsync("Base.Describe", "ignored", false);
        }

        var output = stderr.ToString();
        Assert.Contains("virtual", output);
        Assert.Equal(1, Environment.ExitCode);

        Environment.ExitCode = 0;
        Console.SetError(new StreamWriter(Stream.Null));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Call sites updated across multiple files
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task rewrites_call_sites_to_use_the_type_name()
    {
        using var proj = new AdhocProjectBuilder("StaticApp");
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
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await MakeStaticCommand.ExecuteAsync("Helper.Format", "ignored", false);
        }

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
    public async Task does_not_modify_files()
    {
        using var proj = new AdhocProjectBuilder("StaticApp");
        proj.AddSource("Util.cs", """
                                  class Util {
                                      public int Square(int x) { return x * x; }
                                  }
                                  """);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Util.cs");

        var sw = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            using (WorkspaceLoader.UseOverride(workspace, sln))
            {
                await MakeStaticCommand.ExecuteAsync("Util.Square", "ignored", true);
            }
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
