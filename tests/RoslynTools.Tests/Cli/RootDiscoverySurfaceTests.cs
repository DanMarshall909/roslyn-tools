using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public sealed class RootDiscoverySurfaceTests
{
    [Fact]
    public async Task root_help_shows_agent_entry_points_once_without_maintainer_controls()
    {
        var help = await RtCliHost.RunAsync("--help");

        help.ExitCode.ShouldBe(0);
        help.Stdout.ShouldContain("Start here");
        help.Stdout.Split("rt prime").Length.ShouldBe(2);
        help.Stdout.Split("rt suggest \"<task>\" --top 3").Length.ShouldBe(2);
        help.Stdout.Split("rt dump-help --compact").Length.ShouldBe(2);
        help.Stdout.ShouldNotContain("rt maintainer");
        help.Stdout.ShouldNotContain("prepare-operation");
    }

    [Fact]
    public async Task dump_help_has_usable_drill_in_help_while_compact_catalog_stays_focused()
    {
        var help = await RtCliHost.RunAsync("dump-help --help");
        var compact = await RtCliHost.RunAsync("dump-help --compact");

        help.ExitCode.ShouldBe(0);
        help.Stdout.ShouldContain("--compact");
        compact.ExitCode.ShouldBe(0);
        compact.Stdout.ShouldNotContain("Start here");
        compact.Stdout.ShouldNotContain("rt prime");
        compact.Stdout.ShouldNotContain("rt maintainer");
    }
}
