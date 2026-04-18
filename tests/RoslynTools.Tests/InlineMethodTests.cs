using RoslynTools.Commands;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class InlineMethodTests
{
    private const string MathHelperSource = """
        namespace MathApp;

        public class MathHelper
        {
            public static int Double(int x) => x * 2;
        }

        public class Calc
        {
            void Run()
            {
                var a = MathHelper.Double(5);
                var b = MathHelper.Double(10);
            }
        }
        """;

    [Fact]
    public async Task InlineMethod_SingleExpressionBody_ReplacedAtCallSite()
    {
        using var project = new TestProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var slnPath = await project.BuildAsync();

        await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", slnPath, dryRun: false);

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("var a = 5 * 2;", content);
        Assert.Contains("var b = 10 * 2;", content);
    }

    [Fact]
    public async Task InlineMethod_BlockBodySingleReturn_InlinedCorrectly()
    {
        using var project = new TestProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", """
            namespace MathApp;

            public class MathHelper
            {
                public static int Triple(int x) { return x * 3; }
            }

            public class Calc
            {
                void Run()
                {
                    var result = MathHelper.Triple(4);
                }
            }
            """);
        var slnPath = await project.BuildAsync();

        await InlineMethodCommand.ExecuteAsync("MathHelper", "Triple", slnPath, dryRun: false);

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("4 * 3", content);
    }

    [Fact]
    public async Task InlineMethod_MethodDeletedAfterInlining()
    {
        using var project = new TestProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var slnPath = await project.BuildAsync();

        await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", slnPath, dryRun: false);

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.DoesNotContain("public int Double(", content);
    }

    [Fact]
    public async Task InlineMethod_MultipleCallSites_AllInlined()
    {
        using var project = new TestProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", """
            namespace MathApp;

            public class MathHelper
            {
                public static int Square(int x) => x * x;
            }

            public class Calc
            {
                void Run()
                {
                    var a = MathHelper.Square(2);
                    var b = MathHelper.Square(3);
                    var c = MathHelper.Square(4);
                }
            }
            """);
        var slnPath = await project.BuildAsync();

        await InlineMethodCommand.ExecuteAsync("MathHelper", "Square", slnPath, dryRun: false);

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("2 * 2", content);
        Assert.Contains("3 * 3", content);
        Assert.Contains("4 * 4", content);
        Assert.DoesNotContain("public int Square(", content);
    }

    [Fact]
    public async Task InlineMethod_DryRun_NoWrites()
    {
        using var project = new TestProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var slnPath = await project.BuildAsync();

        var originalContent = await project.ReadSourceAsync("MathHelper.cs");

        await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", slnPath, dryRun: true);

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Equal(originalContent, afterContent);
    }
}
