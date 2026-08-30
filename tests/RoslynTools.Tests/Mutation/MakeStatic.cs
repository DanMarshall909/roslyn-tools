using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class MakeStatic : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public MakeStatic(RtServicesFixture services) => _services = services;

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
            await ExecuteAsync("Calc.Add");
        }

        var content = await proj.ReadSourceAsync("Calc.cs");
        content.ShouldContain("public static int Add", Case.Sensitive);
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var result = await ExecuteResultAsync("Service.GetValue");
            result.IsErr.ShouldBeTrue();
            result.Match(_ => string.Empty, error => error.Message)
                .ShouldContain("this", Case.Sensitive);
        }
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var result = await ExecuteResultAsync("Repo.GetCount");
            result.IsErr.ShouldBeTrue();
            var message = result.Match(_ => string.Empty, error => error.Message);
            message.ShouldContain("instance member", Case.Sensitive);
            message.ShouldContain("_count", Case.Sensitive);
        }
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var result = await ExecuteResultAsync("Base.Describe");
            result.IsErr.ShouldBeTrue();
            result.Match(_ => string.Empty, error => error.Message)
                .ShouldContain("virtual", Case.Sensitive);
        }
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
            await ExecuteAsync("Helper.Format");
        }

        var helperContent = await proj.ReadSourceAsync("Helper.cs");
        helperContent.ShouldContain("public static string Format", Case.Sensitive);

        var appContent = await proj.ReadSourceAsync("App.cs");
        appContent.ShouldContain("Helper.Format(", Case.Sensitive);
        appContent.ShouldNotContain("h.Format(", Case.Sensitive);
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

        MutationExecutionResult<MakeStaticResult> execution;
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            execution = await ExecuteAsync("Util.Square", dryRun: true);
        }

        var after = await proj.ReadSourceAsync("Util.cs");
        after.ShouldBe(before);

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("Util.cs");
    }

    private async Task<MutationExecutionResult<MakeStaticResult>> ExecuteAsync(
        string method,
        bool dryRun = false)
    {
        return (await ExecuteResultAsync(method, dryRun)).Unwrap();
    }

    private Task<Result<MutationExecutionResult<MakeStaticResult>, RtError>> ExecuteResultAsync(
        string method,
        bool dryRun = false)
    {
        return _services.Resolve<MakeStaticOperation>().ExecuteAsync(
            new MakeStaticRequest(method, "ignored", dryRun));
    }
}
