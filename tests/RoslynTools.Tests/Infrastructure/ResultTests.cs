using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     Unit tests for the <see cref="Result{T,TError}" /> ADT and its <c>Unwrap</c> bridge.
///     These pin the totality of <c>Match</c>, the success/error predicates, <c>Map</c>'s
///     branch selectivity, and the legacy-compatible throwing behaviour of <c>Unwrap</c>.
/// </summary>
public class ResultTests
{
    [Fact]
    public void ok_reports_is_ok_and_matches_success_branch()
    {
        var result = Result<int, RtError>.Ok(42);

        result.IsOk.ShouldBeTrue();
        result.IsErr.ShouldBeFalse();
        result.Match(value => value, _ => -1).ShouldBe(42);
    }

    [Fact]
    public void err_reports_is_err_and_matches_error_branch()
    {
        var error = RtError.NotFound("nope");
        var result = Result<int, RtError>.Err(error);

        result.IsErr.ShouldBeTrue();
        result.IsOk.ShouldBeFalse();
        result.Match(_ => "ok", e => e.Message).ShouldBe("nope");
    }

    [Fact]
    public void map_transforms_ok_value_only()
    {
        var ok = Result<int, RtError>.Ok(21).Map(v => v * 2);
        ok.Match(v => v, _ => -1).ShouldBe(42);
    }

    [Fact]
    public void map_preserves_error_untouched()
    {
        var error = RtError.AmbiguousMatch("two matches");
        var mapped = Result<int, RtError>.Err(error).Map(v => v * 2);

        mapped.IsErr.ShouldBeTrue();
        mapped.Match(_ => "ok", e => e.Message).ShouldBe("two matches");
    }

    [Fact]
    public void void_match_runs_the_matching_action_branch()
    {
        var seen = "";

        // Block-bodied (void) lambdas bind to the Action-based Match overload, not Match<TResult>.
        Result<int, RtError>.Ok(7).Match(v => { seen = $"ok:{v}"; }, _ => { seen = "err"; });
        seen.ShouldBe("ok:7");

        Result<int, RtError>.Err(RtError.NotFound("x")).Match(_ => { seen = "ok"; }, e => { seen = $"err:{e.Message}"; });
        seen.ShouldBe("err:x");
    }

    [Fact]
    public void unwrap_returns_value_on_ok()
    {
        Result<int, RtError>.Ok(99).Unwrap().ShouldBe(99);
    }

    [Fact]
    public void unwrap_throws_invalid_operation_with_error_message_on_err()
    {
        var result = Result<int, RtError>.Err(RtError.NotFound("No symbol with FQN 'Foo' found in solution."));

        var ex = Should.Throw<InvalidOperationException>(() => result.Unwrap());
        ex.Message.ShouldBe("No symbol with FQN 'Foo' found in solution.");
    }
}
