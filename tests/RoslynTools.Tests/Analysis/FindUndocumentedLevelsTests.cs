using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public sealed class FindUndocumentedLevelsTests
{
    [Fact]
    public void invalid_level_reports_an_input_error_without_ambient_output()
    {
        var error = Should.Throw<ArgumentException>(() =>
            FindUndocumentedService.ParseAccessibilityLevels(["unknown"]));

        error.Message.ShouldContain("Unknown accessibility level: 'unknown'");
    }
}
