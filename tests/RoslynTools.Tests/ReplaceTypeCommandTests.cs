namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class ReplaceTypeCommandTests
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Multi-constructor: all constructors must receive TimeProvider injection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultipleConstructors_AllGetTimeProviderInjected()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("MyService.cs", """
            namespace TestApp;

            public class MyService
            {
                public MyService()
                {
                    var now = DateTime.UtcNow;
                }

                public MyService(string name)
                {
                    var now = DateTime.UtcNow;
                }
            }
            """);

        var slnPath = await project.BuildAsync();

        await App.RunAsync(["replace-datetime", "--solution", slnPath]);

        var content = await project.ReadSourceAsync("MyService.cs");

        // Both constructors should receive the TimeProvider parameter
        Assert.Equal(2, CountOccurrences(content, "TimeProvider timeProvider"));
        // Both bodies should assign it
        Assert.Equal(2, CountOccurrences(content, "_timeProvider = timeProvider;"));
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
