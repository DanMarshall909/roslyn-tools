using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class BatchMutationRunner_Select
{
    private static readonly string[] AllIds = ["aaaaaaaa", "bbbbbbbb", "cccccccc"];
    private static readonly string[] AAndC = ["aaaaaaaa", "cccccccc"];
    private static readonly string[] AAndB = ["aaaaaaaa", "bbbbbbbb"];

    private static Fix MakeFix(string id) =>
        new(id, "test-cmd", $"target-{id}", "kind", Array.Empty<FixFile>());

    private static Fix[] ThreeFixes() =>
        [MakeFix("aaaaaaaa"), MakeFix("bbbbbbbb"), MakeFix("cccccccc")];

    [Fact]
    public void with_no_flags_returns_all_candidates()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: null, skipCsv: null);

        result.Errors.ShouldBeEmpty();
        result.Surviving.Select(f => f.Id).ShouldBe(AllIds);
    }

    [Fact]
    public void apply_narrows_to_listed_ids()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: "aaaaaaaa,cccccccc", skipCsv: null);

        result.Errors.ShouldBeEmpty();
        result.Surviving.Select(f => f.Id).ShouldBe(AAndC);
    }

    [Fact]
    public void skip_drops_listed_ids()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: null, skipCsv: "bbbbbbbb");

        result.Errors.ShouldBeEmpty();
        result.Surviving.Select(f => f.Id).ShouldBe(AAndC);
    }

    [Fact]
    public void both_flags_set_returns_usage_error()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: "aaaaaaaa", skipCsv: "bbbbbbbb");

        result.Surviving.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].ShouldContain("--apply");
        result.Errors[0].ShouldContain("--skip");
    }

    [Fact]
    public void unknown_id_in_apply_returns_error_naming_the_id()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: "deadbeef", skipCsv: null);

        result.Surviving.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].ShouldContain("deadbeef");
    }

    [Fact]
    public void unknown_id_in_skip_returns_error_naming_the_id()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: null, skipCsv: "deadbeef");

        result.Surviving.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].ShouldContain("deadbeef");
    }

    [Fact]
    public void mix_of_known_and_unknown_in_apply_is_an_error()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: "aaaaaaaa,deadbeef", skipCsv: null);

        result.Surviving.ShouldBeEmpty();
        result.Errors[0].ShouldContain("deadbeef");
    }

    [Fact]
    public void empty_apply_csv_yields_empty_surviving_with_no_error()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: "", skipCsv: null);

        result.Errors.ShouldBeEmpty();
        result.Surviving.ShouldBeEmpty();
    }

    [Fact]
    public void whitespace_only_apply_csv_is_treated_as_empty()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: "   ", skipCsv: null);

        result.Errors.ShouldBeEmpty();
        result.Surviving.ShouldBeEmpty();
    }

    [Fact]
    public void apply_trims_whitespace_around_ids()
    {
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: " aaaaaaaa , bbbbbbbb ", skipCsv: null);

        result.Errors.ShouldBeEmpty();
        result.Surviving.Select(f => f.Id).ShouldBe(AAndB);
    }

    [Fact]
    public void multiple_unknown_ids_are_joined_with_a_comma_separator()
    {
        // Pins the ", " separator in the error message — naive concatenation
        // would smush "deadbeef" and "cafebabe" together as "deadbeefcafebabe".
        var candidates = ThreeFixes();

        var result = BatchMutationRunner.Select(candidates, applyCsv: "deadbeef,cafebabe", skipCsv: null);

        result.Errors[0].ShouldContain("deadbeef, cafebabe");
    }
}
