using RoslynTools.Commands;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class AddNullChecksTests
{
    private const string ServiceSource = """
        class Service {
          public void Process(string name, ILogger logger, int count) { }
          public void NoParams() { }
        }
        interface ILogger { void Log(string msg); }
        """;

    private const string ServiceWithExistingCheckSource = """
        class Service {
          public void ProcessWithCheck(string name, ILogger logger, int count) {
            ArgumentNullException.ThrowIfNull(name);
          }
        }
        interface ILogger { void Log(string msg); }
        """;

    [Fact]
    public async Task AddNullChecks_InsertsThrowIfNullForReferenceParams()
    {
        using var project = new TestProjectBuilder("NullApp");
        project.AddSource("Service.cs", ServiceSource);
        var slnPath = await project.BuildAsync();

        await AddNullChecksCommand.ExecuteAsync("Service", "Process", slnPath, dryRun: false);

        var content = await project.ReadSourceAsync("Service.cs");

        Assert.Contains("ArgumentNullException.ThrowIfNull(name)", content);
        Assert.Contains("ArgumentNullException.ThrowIfNull(logger)", content);
    }

    [Fact]
    public async Task AddNullChecks_ValueTypeParams_Skipped()
    {
        using var project = new TestProjectBuilder("NullApp");
        project.AddSource("Service.cs", ServiceSource);
        var slnPath = await project.BuildAsync();

        await AddNullChecksCommand.ExecuteAsync("Service", "Process", slnPath, dryRun: false);

        var content = await project.ReadSourceAsync("Service.cs");

        Assert.DoesNotContain("ThrowIfNull(count)", content);
    }

    [Fact]
    public async Task AddNullChecks_NoParams_NoOp()
    {
        using var project = new TestProjectBuilder("NullApp");
        project.AddSource("Service.cs", ServiceSource);
        var slnPath = await project.BuildAsync();

        var originalContent = await project.ReadSourceAsync("Service.cs");

        await AddNullChecksCommand.ExecuteAsync("Service", "NoParams", slnPath, dryRun: false);

        var afterContent = await project.ReadSourceAsync("Service.cs");
        Assert.Equal(originalContent, afterContent);
    }

    [Fact]
    public async Task AddNullChecks_AlreadyHasCheck_NotDuplicated()
    {
        using var project = new TestProjectBuilder("NullApp");
        project.AddSource("Service.cs", ServiceWithExistingCheckSource);
        var slnPath = await project.BuildAsync();

        await AddNullChecksCommand.ExecuteAsync("Service", "ProcessWithCheck", slnPath, dryRun: false);

        var content = await project.ReadSourceAsync("Service.cs");

        // Count occurrences — should be exactly 1, not 2
        var count = CountOccurrences(content, "ArgumentNullException.ThrowIfNull(name)");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AddNullChecks_DryRun_NoWrites()
    {
        using var project = new TestProjectBuilder("NullApp");
        project.AddSource("Service.cs", ServiceSource);
        var slnPath = await project.BuildAsync();

        var originalContent = await project.ReadSourceAsync("Service.cs");

        var sw = new System.IO.StringWriter();
        var previousOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            await AddNullChecksCommand.ExecuteAsync("Service", "Process", slnPath, dryRun: true);
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        var afterContent = await project.ReadSourceAsync("Service.cs");
        Assert.Equal(originalContent, afterContent);

        var diffOutput = sw.ToString();
        Assert.NotEmpty(diffOutput);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
