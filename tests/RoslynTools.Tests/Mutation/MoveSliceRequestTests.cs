using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class MoveSliceRequestTests
{
    [Fact]
    public void accepts_multiple_explicit_types_and_defaults_to_review()
    {
        var request = new MoveSliceRequest(
            ["Legacy.Result", " Legacy.Service ", "Legacy.Result"],
            Namespace: null,
            ToProject: "Application",
            ToNamespace: "App.Application.Feature");

        var intent = request.Classify();

        request.DryRun.ShouldBeTrue();
        intent.SelectionKind.ShouldBe(MoveSliceSelectionKind.Types);
        intent.Types.ShouldBe(["Legacy.Result", "Legacy.Service"]);
    }

    [Fact]
    public void accepts_a_recursive_namespace_selection()
    {
        var intent = new MoveSliceRequest(
            Types: null,
            Namespace: "Legacy.Domain",
            ToProject: "Domain",
            ToNamespace: "App.Domain").Classify();

        intent.SelectionKind.ShouldBe(MoveSliceSelectionKind.Namespace);
        intent.SourceNamespace.ShouldBe("Legacy.Domain");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Legacy.Service", "Legacy")]
    public void requires_exactly_one_source_selection(string? type, string? sourceNamespace)
    {
        var result = MoveSliceRequestClassifier.Classify(new MoveSliceRequest(
            type is null ? null : [type],
            sourceNamespace,
            "Application",
            "App.Application"));

        result.Match(_ => false, _ => true).ShouldBeTrue();
    }

    [Fact]
    public void project_reference_changes_require_an_explicit_justification()
    {
        var result = MoveSliceRequestClassifier.Classify(new MoveSliceRequest(
            ["Legacy.Service"],
            Namespace: null,
            ToProject: "Application",
            ToNamespace: "App.Application",
            UpdateProjectReferences: true));

        result.Match(_ => false, _ => true).ShouldBeTrue();
        result.Match(
            _ => string.Empty,
            error => error.Message).ShouldContain("justification", Case.Insensitive);
    }

    [Fact]
    public void internals_visible_to_changes_are_explicit_structured_and_justified()
    {
        var intent = new MoveSliceRequest(
            ["Legacy.Service"],
            Namespace: null,
            ToProject: "Application",
            ToNamespace: "App.Application",
            AddInternalsVisibleTo: ["Domain=Application", "Domain=Application"],
            InternalsVisibleToJustification: "The moved internal policy is consumed by Application.")
            .Classify();

        intent.InternalsVisibleToEdits.ShouldBe(
            [new InternalsVisibleToEditRequest("Domain", "Application")]);
    }
}

internal static class MoveSliceRequestTestExtensions
{
    internal static MoveSliceIntent Classify(this MoveSliceRequest request) =>
        MoveSliceRequestClassifier.Classify(request).Match(
            value => value,
            error => throw new ShouldAssertException(error.Message));
}
