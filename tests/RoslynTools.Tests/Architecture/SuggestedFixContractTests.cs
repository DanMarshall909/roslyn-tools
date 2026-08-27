using RoslynTools.Commands.Results;
using RoslynTools.Services.Results;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class SuggestedFixContractTests
{
    [Fact]
    public void suggested_fix_keeps_command_structure_host_neutral_and_renders_at_the_cli_boundary()
    {
        var fix = FixCatalog.ForUnusedSymbol("type", "Example.Service", "App.slnx")[0];

        fix.Command.ShouldBe("refactor safe-delete");
        fix.Arguments.Select(argument => argument.Value).ShouldBe(
            ["--symbol", "Example.Service", "--kind", "type", "--solution", "App.slnx"]);
        fix.CommandLine().ShouldBe(
            "rt refactor safe-delete --symbol Example.Service --kind type --solution \"App.slnx\"");
    }

    [Fact]
    public void duplicate_and_documentation_suggestions_preserve_the_existing_cli_output()
    {
        var duplicate = FixCatalog.ForDuplicateGroup(
            "extractable",
            "src/Service.cs",
            10,
            20,
            null)[0];
        var documentation = FixCatalog.ForUndocumented("App.slnx")[0];

        duplicate.CommandLine().ShouldBe(
            "rt refactor extract-method \"src/Service.cs\" <NewMethodName> --start-line 10 "
            + "--end-line 20 --replace-duplicates --solution <path> --dry-run");
        documentation.CommandLine().ShouldBe(
            "rt refactor add-xml-doc --from-json docs.json --solution \"App.slnx\" --dry-run");
    }

    [Fact]
    public void external_tool_suggestions_do_not_claim_an_rt_command()
    {
        var fix = FixCatalog.ForUndocumented(null).Single(candidate => candidate.Tool == "stylecop");

        fix.Command.ShouldBeNull();
        fix.Arguments.ShouldBeEmpty();
        fix.CommandLine().ShouldBeNull();
    }

    [Fact]
    public void dynamic_dependency_suggestion_is_built_outside_the_cli_host()
    {
        var fix = FixCatalog.ForDynamicDependency(
            "Example.Service",
            "Bootstrap.RegisterServices",
            "App.slnx");

        fix.CommandLine().ShouldBe(
            "rt refactor add-dynamic-dependency --type \"Example.Service\" "
            + "--root \"Bootstrap.RegisterServices\" --solution \"App.slnx\"");
    }
}
