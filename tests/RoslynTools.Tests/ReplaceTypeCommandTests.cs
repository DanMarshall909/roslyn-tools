using RoslynTools.Core;

namespace RoslynTools.Tests;

public class ReplaceTypeCommandTests
{
    [Fact]
    public async Task MultipleConstructors_AllGetTimeProviderInjected()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await App.RunAsync(["replace-datetime", "--solution", "ignored"]);

        var content = await project.ReadSourceAsync("MyService.cs");

        Assert.Equal(2, CountOccurrences(content, "TimeProvider timeProvider"));
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
