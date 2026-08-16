using System.Text.Json;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class MutationExecutionContractTests
{
    [Fact]
    public void dry_run_preserves_planned_files_value_and_diagnostics()
    {
        var diagnostic = new MutationDiagnostic(
            MutationDiagnosticSeverity.Warning,
            "Review the generated constructor.",
            "RT1001",
            new Dictionary<string, string> { ["symbol"] = "Example.Service" });

        var result = MutationExecutionResult<string>.DryRun(
            "plan",
            ["src/Example/Service.cs"],
            [diagnostic]);

        result.Status.ShouldBe(MutationExecutionStatus.DryRun);
        result.Value.ShouldBe("plan");
        result.ChangedFiles.ShouldBe(["src/Example/Service.cs"]);
        result.Diagnostics.ShouldBe([diagnostic]);
    }

    [Fact]
    public void applied_result_is_distinct_from_dry_run()
    {
        var result = MutationExecutionResult<int>.Applied(
            1,
            ["src/Example/Service.cs"]);

        result.Status.ShouldBe(MutationExecutionStatus.Applied);
        result.ChangedFiles.ShouldBe(["src/Example/Service.cs"]);
        result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void no_op_has_no_changed_files_but_can_explain_why()
    {
        var diagnostic = new MutationDiagnostic(
            MutationDiagnosticSeverity.Information,
            "The requested state is already present.");

        var result = MutationExecutionResult<object>.NoOp(diagnostics: [diagnostic]);

        result.Status.ShouldBe(MutationExecutionStatus.NoOp);
        result.ChangedFiles.ShouldBeEmpty();
        result.Diagnostics.ShouldBe([diagnostic]);
    }

    [Fact]
    public void mutation_result_round_trips_as_a_structured_rpc_value()
    {
        var original = MutationExecutionResult<string>.DryRun(
            "plan",
            ["src/Example/Service.cs"],
            [new MutationDiagnostic(MutationDiagnosticSeverity.Warning, "Review this change.")]);

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<MutationExecutionResult<string>>(json);

        roundTripped.ShouldNotBeNull();
        roundTripped!.Status.ShouldBe(original.Status);
        roundTripped.Value.ShouldBe(original.Value);
        roundTripped.ChangedFiles.ShouldBe(original.ChangedFiles);
        roundTripped.Diagnostics.ShouldBe(original.Diagnostics);
    }

    [Fact]
    public void dry_run_and_applied_outcomes_require_changed_file_identity()
    {
        var dryRunException = Should.Throw<ArgumentException>(() =>
            MutationExecutionResult<object>.DryRun(null, []));
        var appliedException = Should.Throw<ArgumentException>(() =>
            MutationExecutionResult<object>.Applied(null, []));

        dryRunException.ParamName.ShouldBe("changedFiles");
        appliedException.ParamName.ShouldBe("changedFiles");
    }

    [Fact]
    public void no_op_rejects_changed_files()
    {
        var exception = Should.Throw<ArgumentException>(() => new MutationExecutionResult<object>(
            MutationExecutionStatus.NoOp,
            null,
            ["src/Example/Service.cs"],
            []));

        exception.ParamName.ShouldBe("changedFiles");
    }

    [Fact]
    public void safety_metadata_distinguishes_normal_mutations_from_review_first_mutations()
    {
        CommandSafety.Mutation.Effect.ShouldBe(CommandEffect.Mutation);
        CommandSafety.Mutation.MutationDefault.ShouldBe(MutationExecutionDefault.Apply);
        CommandSafety.Mutation.SupportsDryRun.ShouldBeTrue();

        CommandSafety.ReviewByDefault.Effect.ShouldBe(CommandEffect.Mutation);
        CommandSafety.ReviewByDefault.MutationDefault.ShouldBe(MutationExecutionDefault.Review);
        CommandSafety.ReviewByDefault.SupportsDryRun.ShouldBeTrue();
    }

    [Fact]
    public void read_only_safety_has_no_mutation_default_or_dry_run()
    {
        CommandSafety.ReadOnly.Effect.ShouldBe(CommandEffect.ReadOnly);
        CommandSafety.ReadOnly.MutationDefault.ShouldBeNull();
        CommandSafety.ReadOnly.SupportsDryRun.ShouldBeFalse();
    }

    [Fact]
    public void safety_metadata_rejects_incoherent_combinations()
    {
        Should.Throw<ArgumentException>(() => new CommandSafety(
            CommandEffect.ReadOnly,
            MutationExecutionDefault.Review,
            supportsDryRun: true));

        Should.Throw<ArgumentException>(() => new CommandSafety(
            CommandEffect.Mutation,
            mutationDefault: null,
            supportsDryRun: false));
    }
}
