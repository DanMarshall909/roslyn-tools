using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public sealed class LanguageFeaturesTests
{
    public static TheoryData<string, string> UnsafePrimaryConstructorCandidates => new()
    {
        {
            "struct",
            "public struct Candidate { private readonly int _value; public Candidate(int value) { _value = value; } }"
        },
        {
            "record",
            "public record Candidate { private readonly int _value; public Candidate(int value) { _value = value; } }"
        },
        {
            "abstract class",
            "public abstract class Candidate { private readonly int _value; public Candidate(int value) { _value = value; } }"
        },
        {
            "partial class",
            "public partial class Candidate { private readonly int _value; public Candidate(int value) { _value = value; } }"
        },
        {
            "zero parameters",
            "public class Candidate { public Candidate() { } }"
        },
        {
            "multiple constructors",
            "public class Candidate { private readonly int _value; public Candidate(int value) { _value = value; } public Candidate(string value) { _value = value.Length; } }"
        },
        {
            "constructor initializer",
            "public class Candidate { private readonly int _value; public Candidate(int value) : base() { _value = value; } }"
        },
        {
            "constructor attribute",
            "public class Candidate { private readonly int _value; [System.Obsolete] public Candidate(int value) { _value = value; } }"
        },
        {
            "constructor documentation",
            "public class Candidate { private readonly int _value; /// <summary>Creates it.</summary>\n public Candidate(int value) { _value = value; } }"
        },
        {
            "non-public constructor",
            "public class Candidate { private readonly int _value; internal Candidate(int value) { _value = value; } }"
        },
        {
            "constructor modifier",
            "public class Candidate { private readonly int _value; public unsafe Candidate(int value) { _value = value; } }"
        },
        {
            "parameter attribute",
            "public class Candidate { private readonly int _value; public Candidate([System.Runtime.InteropServices.In] int value) { _value = value; } }"
        },
        {
            "parameter modifier",
            "public class Candidate { private readonly int _value; public Candidate(in int value) { _value = value; } }"
        },
        {
            "parameter default",
            "public class Candidate { private readonly int _value; public Candidate(int value = 1) { _value = value; } }"
        },
        {
            "non-object base class",
            "public class Base { } public class Candidate : Base { private readonly int _value; public Candidate(int value) { _value = value; } }"
        },
        {
            "observable body",
            "public class Candidate { private readonly int _value; public Candidate(int value) { System.Console.WriteLine(value); _value = value; } }"
        },
        {
            "transformed assignment",
            "public class Candidate { private readonly string _value; public Candidate(string value) { _value = value ?? throw new System.ArgumentNullException(nameof(value)); } }"
        },
        {
            "parameter reassignment",
            "public class Candidate { private readonly int _value; public Candidate(int value) { value = 1; _value = value; } }"
        },
        {
            "instance initializer",
            "public class Candidate { private readonly int _value; private readonly int _other = 1; public Candidate(int value) { _value = value; } }"
        },
        {
            "required member",
            "public class Candidate { private readonly int _value; public required string Name { get; init; } public Candidate(int value) { _value = value; } }"
        },
        {
            "member name conflict",
            "public class Candidate { private readonly int _value; private int value; public Candidate(int value) { _value = value; } }"
        },
        {
            "property target",
            "public class Candidate { public int Value { get; } public Candidate(int value) { Value = value; } }"
        },
        {
            "mutable field target",
            "public class Candidate { private int _value; public Candidate(int value) { _value = value; } }"
        },
        {
            "unused parameter",
            "public class Candidate { private readonly int _value; public Candidate(int value, int unused) { _value = value; } }"
        },
        {
            "constructor directive",
            "public class Candidate { private readonly int _value;\n#if DEBUG\n public Candidate(int value) { _value = value; }\n#endif\n }"
        }
    };

    [Fact]
    public void Catalogue_has_stable_unique_ids_and_complete_metadata()
    {
        LanguageFeatureCatalog.Version.ShouldBe("csharp-v1");
        LanguageFeatureCatalog.Definitions.Count.ShouldBeGreaterThanOrEqualTo(50);
        LanguageFeatureCatalog.Definitions.Select(feature => feature.Id)
            .ShouldBeUnique();

        LanguageFeatureCatalog.Definitions.ShouldAllBe(feature =>
            !string.IsNullOrWhiteSpace(feature.Id)
            && !string.IsNullOrWhiteSpace(feature.Name)
            && !string.IsNullOrWhiteSpace(feature.Category)
            && !string.IsNullOrWhiteSpace(feature.IntroducedIn)
            && !string.IsNullOrWhiteSpace(feature.DetectorKind)
            && !string.IsNullOrWhiteSpace(feature.CountingBasis));

        LanguageFeatureCatalog.Definitions.Single(feature => feature.Id == "file_scoped_namespace")
            .CountingBasis.ShouldBe("one file-scoped namespace declaration");
        LanguageFeatureCatalog.Definitions.Single(feature => feature.Id == "extension_method_invocation")
            .DetectorKind.ShouldBe("semantic");
        LanguageFeatureCatalog.Definitions.Single(feature => feature.Id == "nullable_reference_types")
            .DetectorKind.ShouldBe("project-option");
        LanguageFeatureCatalog.Definitions.Single(feature => feature.Id == "union_type")
            .DetectorKind.ShouldBe("unsupported");
    }

    [Fact]
    public async Task AnalyzeAsync_reports_density_adoption_and_refactoring_exceptions()
    {
        using var project = new AdhocProjectBuilder("Features");
        project.AddSource("Modern.cs", """"
            namespace Features;

            public record Person(string Name)
            {
                public required string Label { get; init; }
            }

            public static class TextExtensions
            {
                public static string Twice(this string value) => value + value;
            }

            public class Uses
            {
                public async Task<Person> RunAsync()
                {
                    await Task.CompletedTask;
                    int[] values = [1, 2, 3];
                    var text = "x".Twice();
                    var raw = """raw""";
                    return new Person(text) with { Label = raw };
                }
            }
            """");
        project.AddSource("Legacy.cs", """
            namespace Features.Legacy
            {
                public class LegacyType { }
            }
            """);
        var (_, solution) = project.Build();

        var result = await new LanguageFeaturesAnalyzer().AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(IncludeLocations: true));

        result.CatalogVersion.ShouldBe("csharp-v1");
        result.GeneratedCode.ShouldBe("excluded");
        result.Totals.ShouldBe(new LanguageFeatureTotals(1, 2, 24));
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "unsupported_detector");

        var fileScoped = result.Features.Single(feature => feature.Id == "file_scoped_namespace");
        fileScoped.Occurrences.ShouldBe(1);
        fileScoped.Files.ShouldBe(1);
        fileScoped.FilePercent.ShouldBe(50d);
        fileScoped.OccurrencesPerKloc.ShouldBe(41.667d);
        fileScoped.Adoption.ShouldNotBeNull();
        fileScoped.Adoption.Numerator.ShouldBe(1);
        fileScoped.Adoption.Denominator.ShouldBe(2);
        fileScoped.Adoption.Percent.ShouldBe(50d);
        fileScoped.Adoption.Exceptions.ShouldBe(1);
        fileScoped.Adoption.ExceptionLocations.ShouldHaveSingleItem().Path.ShouldEndWith("Legacy.cs");
        fileScoped.Refactoring.ShouldNotBeNull();
        fileScoped.Refactoring.IsAutomatic.ShouldBeFalse();
        fileScoped.Refactoring.ReviewCandidates.ShouldBe(1);
        fileScoped.Refactoring.CandidateLocations.ShouldHaveSingleItem().Line.ShouldBe(1);

        result.Features.Single(feature => feature.Id == "collection_expression").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "raw_string_literal").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "required_member").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "init_accessor").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "record_declaration").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "with_expression").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "async_method").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "await_expression").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "extension_method_invocation").Occurrences.ShouldBe(1);

        var projectResult = result.Projects.ShouldHaveSingleItem();
        projectResult.Name.ShouldBe("Features");
        projectResult.LanguageVersion.ShouldBe("14.0");
        projectResult.EffectiveLanguageVersion.ShouldNotBeNullOrWhiteSpace();
        projectResult.Features.Single(feature => feature.Id == "file_scoped_namespace")
            .Occurrences.ShouldBe(1);
    }

    [Fact]
    public async Task AnalyzeAsync_reports_conservative_primary_constructor_adoption_and_candidates()
    {
        using var project = new AdhocProjectBuilder("PrimaryConstructors");
        project.AddSource("PrimaryConstructors.cs", """
            public interface IDependency { }

            public sealed class ExistingPrimary(IDependency dependency) { }

            public sealed class Candidate : System.IDisposable
            {
                private readonly IDependency _dependency;
                private readonly string _name;

                public Candidate(IDependency dependency, string name)
                {
                    _dependency = dependency;
                    this._name = name;
                }

                public void Dispose() { }
            }
            """);
        project.AddProject("OtherProject").AddSource("OtherCandidate.cs", """
            public sealed class OtherCandidate
            {
                private readonly int _value;
                public OtherCandidate(int value) { _value = value; }
            }
            """);
        var (_, solution) = project.Build();

        var result = await new LanguageFeaturesAnalyzer().AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(
                Feature: "primary_constructor",
                IncludeLocations: true));

        var feature = result.Features.ShouldHaveSingleItem();
        feature.Occurrences.ShouldBe(1);
        feature.Adoption.ShouldNotBeNull();
        feature.Adoption.Numerator.ShouldBe(1);
        feature.Adoption.Denominator.ShouldBe(3);
        feature.Adoption.Opportunity.ShouldBe(
            "existing non-record primary constructors and conservatively eligible class constructor shapes");
        feature.Adoption.Percent.ShouldBe(33.333d);
        feature.Adoption.Exceptions.ShouldBe(2);
        feature.Adoption.ExceptionLocations!.Count.ShouldBe(2);
        feature.Adoption.ExceptionLocations.ShouldContain(location =>
            location.Project == "PrimaryConstructors" && location.Line == 10);
        feature.Adoption.ExceptionLocations.ShouldContain(location =>
            location.Project == "OtherProject" && location.Line == 4);
        feature.Refactoring.ShouldNotBeNull();
        feature.Refactoring.IsAutomatic.ShouldBeFalse();
        feature.Refactoring.ReviewCandidates.ShouldBe(2);
        feature.Refactoring.Description.ShouldContain("retain readonly fields");
        feature.Refactoring.Description.ShouldContain("Do not replace field uses");
        feature.Refactoring.CandidateLocations!.Count.ShouldBe(2);

        var projectFeature = result.Projects.Single(project => project.Name == "PrimaryConstructors")
            .Features.ShouldHaveSingleItem();
        projectFeature.Id.ShouldBe("primary_constructor");
        projectFeature.Adoption.ShouldNotBeNull();
        projectFeature.Adoption.Denominator.ShouldBe(2);
        projectFeature.Adoption.Exceptions.ShouldBe(1);
        projectFeature.Adoption.ExceptionLocations.ShouldHaveSingleItem().Line.ShouldBe(10);
        var otherProjectFeature = result.Projects.Single(project => project.Name == "OtherProject")
            .Features.ShouldHaveSingleItem();
        otherProjectFeature.Occurrences.ShouldBe(0);
        otherProjectFeature.Adoption!.Denominator.ShouldBe(1);
        otherProjectFeature.Adoption.Exceptions.ShouldBe(1);

        var bounded = await new LanguageFeaturesAnalyzer().AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(
                Feature: "primary_constructor",
                RefactorableOnly: true));
        bounded.Features.ShouldHaveSingleItem().Locations.ShouldBeNull();
        bounded.Features[0].Adoption!.ExceptionLocations.ShouldBeNull();
        bounded.Features[0].Refactoring!.CandidateLocations.ShouldBeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_does_not_offer_primary_constructors_before_csharp_12()
    {
        using var project = new AdhocProjectBuilder("CSharp11");
        project.AddSource("Candidate.cs", """
            public sealed class Candidate
            {
                private readonly int _value;
                public Candidate(int value) { _value = value; }
            }
            """);
        var (_, solution) = project.Build();
        var projectId = solution.Projects.ShouldHaveSingleItem().Id;
        var parseOptions = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
        solution = solution.WithProjectParseOptions(
            projectId,
            parseOptions.WithLanguageVersion(LanguageVersion.CSharp11));

        var result = await new LanguageFeaturesAnalyzer().AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(Feature: "primary_constructor"));

        result.Features.ShouldBeEmpty();
        result.Projects.ShouldHaveSingleItem().LanguageVersion.ShouldBe("11.0");
    }

    [Theory]
    [MemberData(nameof(UnsafePrimaryConstructorCandidates))]
    public async Task AnalyzeAsync_excludes_unsafe_primary_constructor_candidates(
        string reason,
        string source)
    {
        using var project = new AdhocProjectBuilder("UnsafePrimaryConstructor");
        project.AddSource(reason.Replace(' ', '_') + ".cs", source);
        var (_, solution) = project.Build();

        var result = await new LanguageFeaturesAnalyzer().AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(Feature: "primary_constructor"));

        result.Diagnostics.ShouldNotContain(diagnostic => diagnostic.Code == "parse_error", reason);
        result.Features.ShouldBeEmpty(reason);
    }

    [Fact]
    public async Task AnalyzeAsync_detects_supported_csharp_13_and_14_features()
    {
        using var project = new AdhocProjectBuilder("Recent");
        project.AddSource("Recent.cs", """
            using System;
            using System.Collections.Generic;

            static class Extensions
            {
                extension(string value)
                {
                    public string Twice() => value + value;
                }
            }

            partial class Recent
            {
                partial int Value { get; }
                partial int Value => 1;
                partial int this[int index] { get; }
                partial int this[int index] => index;
                partial event Action? Changed;
                partial event Action? Changed { add { } remove { } }
                partial Recent();
                partial Recent() { }
                public string Name { get => field; set => field = value; }
                void Collect(params IEnumerable<int> values) { }
                void Assign(Recent? item) => item?.Name = "\e";
                string GenericName() => nameof(List<>);
                void Lambda() { RefAction action = (ref int value) => value++; }
                delegate void RefAction(ref int value);
            }
            """);
        var (_, solution) = project.Build();

        var result = await new LanguageFeaturesAnalyzer().AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest());

        result.Features.Single(feature => feature.Id == "params_collection").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "escape_character").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "partial_property").Occurrences.ShouldBe(2);
        result.Features.Single(feature => feature.Id == "partial_indexer").Occurrences.ShouldBe(2);
        result.Features.Single(feature => feature.Id == "partial_event").Occurrences.ShouldBe(2);
        result.Features.Single(feature => feature.Id == "partial_constructor").Occurrences.ShouldBe(2);
        result.Features.Single(feature => feature.Id == "extension_block").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "field_backed_property").Occurrences.ShouldBe(2);
        result.Features.Single(feature => feature.Id == "null_conditional_assignment").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "unbound_generic_nameof").Occurrences.ShouldBe(1);
        result.Features.Single(feature => feature.Id == "lambda_parameter_modifier").Occurrences.ShouldBe(1);
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == "unsupported_detector"
            && diagnostic.Message.Contains("union_type", StringComparison.Ordinal));
        result.Diagnostics.ShouldNotContain(diagnostic => diagnostic.Code == "parse_error");
    }

    [Fact]
    public async Task AnalyzeAsync_excludes_generated_sources_by_default_and_can_include_them()
    {
        using var project = new AdhocProjectBuilder("Generated");
        project.AddSource("HandWritten.cs", "namespace HandWritten; public record Value(int Id);");
        project.AddSource("Generated.g.cs", "namespace Generated; public record GeneratedValue(int Id);");
        project.AddSource("Form.Designer.cs", "namespace Designer; public record DesignerValue(int Id);");
        project.AddSource("Marked.cs", "// <auto-generated>\nnamespace Marked; public record MarkedValue(int Id);");
        project.AddSource("obj/Debug/Intermediate.cs", "namespace Intermediate; public record Temp(int Id);");
        var (_, solution) = project.Build();
        var analyzer = new LanguageFeaturesAnalyzer();

        var excluded = await analyzer.AnalyzeAsync(solution, new LanguageFeaturesRequest());
        var included = await analyzer.AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(IncludeGenerated: true));

        excluded.Totals.Files.ShouldBe(1);
        excluded.Features.Single(feature => feature.Id == "record_declaration").Occurrences.ShouldBe(1);
        included.Totals.Files.ShouldBe(5);
        included.Features.Single(feature => feature.Id == "record_declaration").Occurrences.ShouldBe(5);
        included.GeneratedCode.ShouldBe("included");
    }

    [Fact]
    public async Task AnalyzeAsync_counts_linked_documents_once_per_project()
    {
        using var project = new AdhocProjectBuilder("ProjectA");
        project.AddSource("Shared.cs", "namespace Shared; public record Value(int Id);");
        project.AddProject("ProjectB");
        var (_, solution) = project.Build();
        var sharedDocument = solution.Projects.Single(candidate => candidate.Name == "ProjectA")
            .Documents.ShouldHaveSingleItem();
        var secondProject = solution.Projects.Single(candidate => candidate.Name == "ProjectB");
        solution = solution.AddDocument(
            DocumentId.CreateNewId(secondProject.Id),
            "Shared.cs",
            SourceText.From("namespace Shared; public record Value(int Id);"),
            filePath: sharedDocument.FilePath);

        var result = await new LanguageFeaturesAnalyzer().AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(Feature: "record_declaration"));

        result.Totals.Files.ShouldBe(2);
        result.Features.ShouldHaveSingleItem().Occurrences.ShouldBe(2);
        result.Features[0].Files.ShouldBe(2);
        result.Features[0].FilePercent.ShouldBe(100d);
    }

    [Fact]
    public async Task AnalyzeAsync_validates_filters_bounds_payloads_and_never_false_zeros_unsupported_features()
    {
        using var project = new AdhocProjectBuilder("Validation");
        project.AddSource("Features.cs", """
            namespace Validation { record Value(int Id); class C { int Value { get; set; } } }
            """);
        var (_, solution) = project.Build();
        var analyzer = new LanguageFeaturesAnalyzer();

        var top = await analyzer.AnalyzeAsync(solution, new LanguageFeaturesRequest(Top: 1));
        var unknownFeature = await analyzer.AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(Feature: "does_not_exist"));
        var unknownCategory = await analyzer.AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(Category: "does_not_exist"));
        var invalidVersion = await analyzer.AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(IntroducedSince: "tomorrow"));
        var invalidTop = await analyzer.AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(Top: -1));
        var unsupported = await analyzer.AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(Feature: "union_type"));
        var withoutLocations = await analyzer.AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest(Feature: "init_accessor"));

        top.Features.Count.ShouldBe(1);
        unknownFeature.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "unknown_feature");
        unknownCategory.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "unknown_category");
        invalidVersion.Features.ShouldBeEmpty();
        invalidVersion.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "invalid_version");
        invalidTop.Features.ShouldBeEmpty();
        invalidTop.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "invalid_top");
        unsupported.Features.ShouldBeEmpty();
        unsupported.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == "unsupported_detector"
            && diagnostic.Message.Contains("union_type", StringComparison.Ordinal));
        withoutLocations.Features.ShouldHaveSingleItem().Locations.ShouldBeNull();
        withoutLocations.Features[0].Adoption!.ExceptionLocations.ShouldBeNull();
        withoutLocations.Features[0].Refactoring!.CandidateLocations.ShouldBeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_filters_after_calculating_metrics_and_returns_refactorable_rows()
    {
        using var project = new AdhocProjectBuilder("Filters");
        project.AddSource("Features.cs", """
            namespace Filters
            {
                class C
                {
                    int Value { get; set; }
                    object M(int[] xs) => xs switch { [] => new(), _ => new object() };
                    int Legacy(int value)
                    {
                        switch (value) { case 0: return 1; default: return 2; }
                    }
                }
            }
            """);
        var (_, solution) = project.Build();
        var analyzer = new LanguageFeaturesAnalyzer();

        var feature = await analyzer.AnalyzeAsync(solution, new LanguageFeaturesRequest(
            Feature: "switch_expression",
            IncludeLocations: true));
        var category = await analyzer.AnalyzeAsync(solution, new LanguageFeaturesRequest(
            Category: "pattern",
            IntroducedSince: "11.0"));
        var refactorable = await analyzer.AnalyzeAsync(solution, new LanguageFeaturesRequest(
            RefactorableOnly: true));

        feature.Features.ShouldHaveSingleItem().Id.ShouldBe("switch_expression");
        feature.Features[0].Occurrences.ShouldBe(1);
        feature.Features[0].Locations.ShouldHaveSingleItem();
        category.Features.ShouldHaveSingleItem().Id.ShouldBe("list_pattern");
        refactorable.Features.Select(result => result.Id).ShouldBe([
            "switch_expression",
            "file_scoped_namespace",
            "init_accessor"
        ]);
        refactorable.Features.All(feature =>
                feature.Refactoring is { IsAutomatic: false, Command: null, ReviewCandidates: 1 })
            .ShouldBeTrue();
        refactorable.Features.Single(feature => feature.Id == "init_accessor")
            .Refactoring!.Description.ShouldContain("usage analysis");
        refactorable.Features.Single(feature => feature.Id == "switch_expression")
            .Refactoring!.Description.ShouldContain("control flow");
    }

    [Fact]
    public async Task AnalyzeAsync_reports_parse_errors_as_partial_diagnostics()
    {
        using var project = new AdhocProjectBuilder("Broken");
        project.AddSource("Good.cs", "namespace Good; record GoodValue(int Id);");
        project.AddSource("Broken.cs", "namespace Broken { class C { void M( } }");
        var (_, solution) = project.Build();

        var result = await new LanguageFeaturesAnalyzer().AnalyzeAsync(
            solution,
            new LanguageFeaturesRequest());

        result.Totals.Files.ShouldBe(2);
        result.Features.Single(feature => feature.Id == "record_declaration").Occurrences.ShouldBe(1);
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == "parse_error"
            && diagnostic.Project == "Broken"
            && diagnostic.Path!.EndsWith("Broken.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Operation_loads_requested_solution_renders_text_and_honors_cancellation()
    {
        using var project = new AdhocProjectBuilder("Operation");
        project.AddSource("Value.cs", "namespace Operation; public record Value(int Id);");
        var (workspace, solution) = project.Build();
        var sessionFactory = new FixedWorkspaceSessionFactory(workspace, solution);
        var operation = new LanguageFeaturesOperation(sessionFactory, new LanguageFeaturesAnalyzer());

        var result = await operation.ExecuteAsync(new LanguageFeaturesRequest("requested.slnx"));
        using var writer = new StringWriter();
        result.RenderText(writer);
        using var jsonWriter = new StringWriter();
        result.RenderJson(jsonWriter);

        operation.CommandName.ShouldBe("language_features");
        sessionFactory.LoadedSolutionPath.ShouldBe("requested.slnx");
        writer.ToString().ShouldContain("C# language features");
        writer.ToString().ShouldContain("record_declaration");
        jsonWriter.ToString().ShouldContain("\"catalogVersion\"");
        jsonWriter.ToString().Contains("\"CatalogVersion\"", StringComparison.Ordinal).ShouldBeFalse();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            operation.ExecuteAsync(new LanguageFeaturesRequest(), cancellation.Token));
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public string? LoadedSolutionPath { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
        {
            LoadedSolutionPath = solutionPath;
            return Task.FromResult((workspace, solution));
        }
    }
}
