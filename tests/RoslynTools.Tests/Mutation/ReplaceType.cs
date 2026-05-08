using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public class ReplaceType
{
    [Fact]
    public async Task injects_timeprovider_into_all_constructors()
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
        {
            await App.RunAsync(["replace-datetime", "--solution", "ignored"]);
        }

        var content = await project.ReadSourceAsync("MyService.cs");

        Assert.Equal(2, CountOccurrences(content, "TimeProvider timeProvider"));
        Assert.Equal(2, CountOccurrences(content, "_timeProvider = timeProvider;"));
    }

    [Fact]
    public async Task replaces_local_time_with_local_time_provider_calls()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Clock.cs", """
                                      namespace TestApp;

                                      public class Clock
                                      {
                                          public DateTime Read() => DateTime.Now;
                                      }
                                      """);

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new ReplaceTypeService(NullLogger<ReplaceTypeService>.Instance, writer)
            .RunAsync(solution, false);

        var content = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Clock.cs");

        Assert.Contains("_timeProvider.GetLocalNow().DateTime", content);
        Assert.Contains("private readonly TimeProvider _timeProvider;", content);
        Assert.Contains("public Clock(TimeProvider timeProvider)", content);
    }

    [Fact]
    public async Task leave_the_solution_unchanged_when_no_datetime_usage_exists()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Clock.cs", """
                                      namespace TestApp;

                                      public class Clock
                                      {
                                          public string Read() => "none";
                                      }
                                      """);

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new ReplaceTypeService(NullLogger<ReplaceTypeService>.Instance, writer)
            .RunAsync(solution, false);

        Assert.Null(writer.Solution);
    }

    [Fact]
    public async Task leave_static_classes_without_constructor_injection()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Clock.cs", """
                                      namespace TestApp;

                                      public static class Clock
                                      {
                                          public static DateTime Read() => DateTime.UtcNow;
                                      }
                                      """);

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new ReplaceTypeService(NullLogger<ReplaceTypeService>.Instance, writer)
            .RunAsync(solution, false);

        var content = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Clock.cs");

        Assert.Contains("_timeProvider.GetUtcNow().UtcDateTime", content);
        Assert.DoesNotContain("private readonly TimeProvider _timeProvider;", content);
        Assert.DoesNotContain("Clock(TimeProvider timeProvider)", content);
    }

    [Fact]
    public async Task keep_existing_time_provider_fields()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Clock.cs", """
                                      namespace TestApp;

                                      public class Clock
                                      {
                                          private readonly TimeProvider _timeProvider;

                                          public Clock(TimeProvider timeProvider)
                                          {
                                              _timeProvider = timeProvider;
                                          }

                                          public DateTime Read() => DateTime.UtcNow;
                                      }
                                      """);

        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();

        await new ReplaceTypeService(NullLogger<ReplaceTypeService>.Instance, writer)
            .RunAsync(solution, false);

        var content = await TestSolutionDocuments.GetTextAsync(writer.Solution!, "Clock.cs");

        Assert.Contains("_timeProvider.GetUtcNow().UtcDateTime", content);
        Assert.Equal(1, CountOccurrences(content, "private readonly TimeProvider _timeProvider;"));
        Assert.Equal(1, CountOccurrences(content, "TimeProvider timeProvider"));
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
