namespace RoslynTools.Tests.Issues.EP_004_NewRtRefactorCommands;

/// <summary>
/// US-054: Add `rt refactor migrate-to-ioptions --section &lt;name&gt; --name &lt;T&gt;` command.
///
/// Slimmed Option A scope: POCO synthesis with type inference + DI registration
/// insertion + read-site worklist for manual updates. Out of scope (file follow-ups
/// after dogfooding if needed): --key mode, read-site rewrites, primary-ctor and
/// record consumer support, multi-IOptions handling, --decisions YAML round-trip,
/// --namespace and --project flags.
///
/// Estimate: ~250–400 LOC, ~4–6 hours.
/// </summary>
[Trait("Epic", "EP-004")]
[Trait("Story", "US-054")]
public class MigrateToIOptions
{
    [Fact]
    [Trait("AcceptanceCriterion", "1")]
    [Trait("Concern", "PocoGeneration")]
    public void with_section_run_writes_internal_sealed_poco_under_host_project()
        => Assert.Fail("US-054 AC-1: POCO file is `internal sealed`, file-scoped namespace, init-only props, written under the project hosting the detected DI registration site.");

    [Fact]
    [Trait("AcceptanceCriterion", "2")]
    [Trait("Concern", "TypeInference")]
    public void with_section_keys_types_inferred_from_getvalue_and_appsettings()
        => Assert.Fail("US-054 AC-2: GetValue<T>(...) calls and appsettings.json value kinds drive POCO property primitive types (string, int, bool, etc.).");

    [Fact]
    [Trait("AcceptanceCriterion", "3")]
    [Trait("Concern", "DIRegistration")]
    public void with_section_run_inserts_services_configure_at_detected_di_site()
        => Assert.Fail("US-054 AC-3: services.Configure<T>(configuration.GetSection(\"<section>\")) inserted exactly once at the detected DI site; registration file appears in stdout's changed list.");

    [Fact]
    [Trait("AcceptanceCriterion", "4")]
    [Trait("Concern", "ConsumerWorklist")]
    public void with_consuming_files_distributed_stdout_lists_them_sorted_by_path()
        => Assert.Fail("US-054 AC-4: stdout prints worklist of consuming files (sorted by path, invariant culture). Command does NOT rewrite any read site.");

    [Fact]
    [Trait("AcceptanceCriterion", "5")]
    [Trait("Concern", "Validation")]
    public void with_section_omitted_exits_one_with_stderr_message()
        => Assert.Fail("US-054 AC-5: --section omitted → exit 1, stderr indicates --section is required.");

    [Fact]
    [Trait("AcceptanceCriterion", "6")]
    [Trait("Concern", "Validation")]
    public void with_name_omitted_exits_one_with_stderr_message()
        => Assert.Fail("US-054 AC-6: --name omitted → exit 1, stderr indicates --name is required.");

    [Fact]
    [Trait("AcceptanceCriterion", "7")]
    [Trait("Concern", "Validation")]
    public void with_name_not_pascalcase_exits_one_and_writes_no_files()
        => Assert.Fail("US-054 AC-7: --name myfeatureoptions (not PascalCase) → exit 1, stderr says PascalCase required, no files written.");

    [Fact]
    [Trait("AcceptanceCriterion", "8")]
    [Trait("Concern", "Validation")]
    public void with_malformed_section_exits_one_and_writes_no_files()
        => Assert.Fail("US-054 AC-8: --section \"\" or \":foo\" or \"foo::bar\" → exit 1, stderr describes malformed section, no files written.");

    [Fact]
    [Trait("AcceptanceCriterion", "9")]
    [Trait("Concern", "Validation")]
    public void with_name_collides_with_existing_type_exits_one_and_writes_no_files()
        => Assert.Fail("US-054 AC-9: --name collides with an existing non-migration type in the target namespace → exit 1, stderr identifies the collision, no files written.");

    [Fact]
    [Trait("AcceptanceCriterion", "10")]
    [Trait("Concern", "SolutionAutoDetect")]
    public void with_solution_omitted_and_single_slnx_in_cwd_auto_detects()
        => Assert.Fail("US-054 AC-10: --solution auto-detected when cwd contains exactly one .slnx/.sln (matches behaviour of other refactor commands).");

    [Fact]
    [Trait("AcceptanceCriterion", "11")]
    [Trait("Concern", "ErrorHandling")]
    public void with_workspace_load_failure_exits_one_with_no_unhandled_exception()
        => Assert.Fail("US-054 AC-11: --solution NoSuch.slnx (or other load failure) → exit 1 + stderr describing failure; no unhandled exception escapes to the process boundary.");

    [Fact]
    [Trait("AcceptanceCriterion", "12")]
    [Trait("Concern", "DIRegistration")]
    public void with_zero_di_registration_sites_exits_one_with_no_writes()
        => Assert.Fail("US-054 AC-12: zero candidate DI registration sites in the solution → exit 1 + stderr instructing the user to add a services.Configure entry-point manually; no files written.");

    [Fact]
    [Trait("AcceptanceCriterion", "13")]
    [Trait("Concern", "DIRegistration")]
    public void with_multiple_di_registration_sites_picks_first_and_warns()
        => Assert.Fail("US-054 AC-13: multiple candidate DI registration sites → first in deterministic order (path invariant culture, then line); stderr warns and lists all candidates.");

    [Fact]
    [Trait("AcceptanceCriterion", "14")]
    [Trait("Concern", "Idempotence")]
    public void with_re_run_after_success_exits_zero_and_makes_no_changes()
        => Assert.Fail("US-054 AC-14: re-run with identical args after a successful run → exit 0, no file changes, stdout reports the migration as already applied.");

    [Fact]
    [Trait("AcceptanceCriterion", "15")]
    [Trait("Concern", "DryRun")]
    public void with_dry_run_emits_diff_with_synthesized_poco_and_no_writes()
        => Assert.Fail("US-054 AC-15: --dry-run on a fresh solution → DiffPlex unified diff to stdout including the synthesized POCO file as `+++ b/.../<T>.cs`; no files modified; stderr indicates dry-run mode.");

    [Fact]
    [Trait("AcceptanceCriterion", "16")]
    [Trait("Concern", "OutputFormat")]
    public void with_successful_run_stdout_is_plain_text_and_diagnostics_on_stderr()
        => Assert.Fail("US-054 AC-16: stdout is plain text (POCO file created, registration file modified, read-site worklist) — not JSON. Diagnostics/progress messages appear on stderr only.");

    [Fact]
    [Trait("AcceptanceCriterion", "17")]
    [Trait("Concern", "PocoFormatting")]
    public void with_generated_poco_passes_dotnet_format_verify_no_changes()
        => Assert.Fail("US-054 AC-17: generated POCO uses file-scoped namespace, is `internal sealed`, every property is `{ get; init; }` with primitive defaults (string.Empty for string, default for value types). dotnet format --verify-no-changes exits 0.");

    [Fact]
    [Trait("AcceptanceCriterion", "18")]
    [Trait("Concern", "BuildGates")]
    public void with_post_run_state_build_format_and_test_all_pass()
        => Assert.Fail("US-054 AC-18: after a successful run, `dotnet build RoslynTools.slnx -c Release`, `dotnet format RoslynTools.slnx --verify-no-changes`, and `dotnet test RoslynTools.slnx -v minimal` all exit 0; this MigrateToIOptions test class is included.");

    [Fact]
    [Trait("AcceptanceCriterion", "19")]
    [Trait("Concern", "DumpHelp")]
    public void with_dump_help_lists_command_under_refactor_with_correct_metadata()
        => Assert.Fail("US-054 AC-19: rt dump-help lists migrate-to-ioptions under the refactor group with Mutates=true, JsonOutput=false, and Examples containing at least one --section invocation and one --dry-run invocation.");

    [Fact]
    [Trait("AcceptanceCriterion", "20")]
    [Trait("Concern", "Metadata")]
    public void with_command_metadata_category_is_refactor_and_mutates()
        => Assert.Fail("US-054 AC-20: IRtCommand contract — MigrateToIOptionsCommand exposes static RtCommandInfo Info with Category=\"refactor\" and a static Command Build() method, reflection-discovered into the registry; App.cs startup assertion does not throw.");
}
