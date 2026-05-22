using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

/// <summary>
///     Unit tests for pure-logic helper methods extracted into service classes.
///     Integration-level behaviour (workspace write/dry-run) is covered by the
///     existing command-level tests in the same folder.
/// </summary>
public class ServiceHelpers : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ServiceHelpers(RtServicesFixture services) => _services = services;

    // ── FixNamespaceService ───────────────────────────────────────────────

    [Theory]
    [InlineData("C:/proj/src/Services/Foo.cs", "C:/proj/src", "MyApp", "MyApp.Services")]
    [InlineData("C:/proj/src/Foo.cs", "C:/proj/src", "MyApp", "MyApp")]
    [InlineData("C:/proj/src/A/B/C/Foo.cs", "C:/proj/src", "MyApp", "MyApp.A.B.C")]
    public void computes_expected_namespace_from_folder_path(
        string filePath, string projectDir, string assemblyName, string expected)
    {
        // Normalize to OS path separators
        filePath = filePath.Replace('/', Path.DirectorySeparatorChar);
        projectDir = projectDir.Replace('/', Path.DirectorySeparatorChar);

        var result = _services.Resolve<FixNamespaceService>().ComputeExpectedNamespace(filePath, projectDir, assemblyName);
        result.ShouldBe(expected);
    }

    [Fact]
    public void gets_declared_namespace_returns_file_scoped_namespace()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Foo.Bar;");
        var root = tree.GetRoot();
        _services.Resolve<FixNamespaceService>().GetDeclaredNamespace(root).ShouldBe("Foo.Bar");
    }

    [Fact]
    public void gets_declared_namespace_returns_block_scoped_namespace()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Foo.Bar { }");
        var root = tree.GetRoot();
        _services.Resolve<FixNamespaceService>().GetDeclaredNamespace(root).ShouldBe("Foo.Bar");
    }

    [Fact]
    public void gets_declared_namespace_returns_null_when_no_namespace()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Foo { }");
        var root = tree.GetRoot();
        _services.Resolve<FixNamespaceService>().GetDeclaredNamespace(root).ShouldBeNull();
    }

    // ── AddDependencyService ──────────────────────────────────────────────

    [Theory]
    [InlineData("IEmailService", "_emailService")]
    [InlineData("IFoo", "_foo")]
    [InlineData("EmailService", "_emailService")]
    [InlineData("Abc", "_abc")]
    public void derives_field_name_strips_leading_i_for_interfaces(string depType, string expected)
    {
        new AddDependencyService().DeriveFieldName(depType).ShouldBe(expected);
    }

    [Fact]
    public void normalize_type_name_strips_global_prefix()
    {
        new AddDependencyService().NormalizeTypeName("global::Foo.Bar").ShouldBe("Foo.Bar");
        new AddDependencyService().NormalizeTypeName("  Foo  ").ShouldBe("Foo");
    }

    // ── ExtractClassService ───────────────────────────────────────────────

    [Fact]
    public void extracts_class_derive_field_name_returns_camel_case_with_underscore()
    {
        _services.Resolve<ExtractClassService>().DeriveFieldName("MyHelper").ShouldBe("_myHelper");
        _services.Resolve<ExtractClassService>().DeriveFieldName("").ShouldBe("_dependency");
        _services.Resolve<ExtractClassService>().DeriveFieldName("A").ShouldBe("_a");
    }

    [Fact]
    public void extracts_class_normalize_type_name_strips_global_prefix()
    {
        _services.Resolve<ExtractClassService>().NormalizeTypeName("global::My.Type").ShouldBe("My.Type");
    }

    [Fact]
    public void builds_new_class_text_generates_static_class_when_flag_set()
    {
        var text = _services.Resolve<ExtractClassService>().BuildNewClassText(
            "MyApp",
            "Helper",
            true,
            []);

        text.ShouldContain("namespace MyApp;", Case.Sensitive);
        text.ShouldContain("public static class Helper", Case.Sensitive);
    }

    [Fact]
    public void builds_new_class_text_generates_instance_class_without_namespace()
    {
        var text = _services.Resolve<ExtractClassService>().BuildNewClassText(
            "",
            "Helper",
            false,
            []);

        text.ShouldNotContain("namespace", Case.Sensitive);
        text.ShouldContain("public class Helper", Case.Sensitive);
        text.ShouldNotContain("static", Case.Sensitive);
    }

    // ── ExtractInterfaceService ───────────────────────────────────────────

    [Fact]
    public void builds_interface_text_generates_interface_declaration()
    {
        var text = InterfaceTextBuilder.BuildInterfaceText("MyApp", "IFoo", []);

        text.ShouldContain("namespace MyApp;", Case.Sensitive);
        text.ShouldContain("public interface IFoo", Case.Sensitive);
    }

    [Fact]
    public void builds_interface_text_skips_namespace_when_empty()
    {
        var text = InterfaceTextBuilder.BuildInterfaceText("", "IFoo", []);

        text.ShouldNotContain("namespace", Case.Sensitive);
        text.ShouldContain("public interface IFoo", Case.Sensitive);
    }

    // ── ReplaceTypeService — rewriter unit tests ───────────────────────────

    [Fact]
    public void replaces_utc_now_in_date_time_rewriter()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              class Foo { void Bar() { var t = DateTime.UtcNow; } }
                                              """);
        var root = tree.GetRoot();
        var rewriter = new ReplaceTypeService.DateTimeRewriter();
        var newRoot = rewriter.Visit(root);

        rewriter.Changed.ShouldBeTrue();
        newRoot.ToFullString().ShouldContain("_timeProvider.GetUtcNow().UtcDateTime", Case.Sensitive);
    }

    [Fact]
    public void replaces_now_in_date_time_rewriter()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              class Foo { void Bar() { var t = DateTime.Now; } }
                                              """);
        var root = tree.GetRoot();
        var rewriter = new ReplaceTypeService.DateTimeRewriter();
        rewriter.Visit(root);

        rewriter.Changed.ShouldBeTrue();
    }

    [Fact]
    public void tracks_class_name_in_date_time_rewriter()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              class OrderService { void Bar() { var t = DateTime.UtcNow; } }
                                              """);
        var root = tree.GetRoot();
        var rewriter = new ReplaceTypeService.DateTimeRewriter();
        rewriter.Visit(root);

        rewriter.ClassesWithReplacements.ShouldContain("OrderService");
    }

    [Fact]
    public void tracks_struct_name_in_date_time_rewriter()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              struct TimeStruct { void Bar() { var t = DateTime.UtcNow; } }
                                              """);
        var root = tree.GetRoot();
        var rewriter = new ReplaceTypeService.DateTimeRewriter();
        rewriter.Visit(root);

        rewriter.ClassesWithReplacements.ShouldContain("TimeStruct");
    }

    [Fact]
    public void leaves_date_time_rewriter_unchanged_when_no_datetime_usage()
    {
        var tree = CSharpSyntaxTree.ParseText("class Foo { }");
        var root = tree.GetRoot();
        var rewriter = new ReplaceTypeService.DateTimeRewriter();
        rewriter.Visit(root);

        rewriter.Changed.ShouldBeFalse();
    }

    // ── RenameService.ReadBatchRows ───────────────────────────────────────

    [Fact]
    public void reads_batch_rows_throws_for_empty_file()
    {
        var path = WriteTempCsv("");
        Should.Throw<InvalidOperationException>(() => new RenameService().ReadBatchRows(path));
    }

    [Fact]
    public void reads_batch_rows_throws_when_source_symbol_column_missing()
    {
        var path = WriteTempCsv("target_name\nFoo");
        Should.Throw<InvalidOperationException>(() => new RenameService().ReadBatchRows(path));
    }

    [Fact]
    public void reads_batch_rows_throws_when_target_name_column_missing()
    {
        var path = WriteTempCsv("source_symbol\nFoo");
        Should.Throw<InvalidOperationException>(() => new RenameService().ReadBatchRows(path));
    }

    [Fact]
    public void reads_batch_rows_parses_valid_csv()
    {
        var path = WriteTempCsv("source_symbol,target_name\nOldName,NewName");
        var rows = new RenameService().ReadBatchRows(path);
        Assert.Single(rows);
        rows[0].OldFqn.ShouldBe("OldName");
        rows[0].NewName.ShouldBe("NewName");
        rows[0].Review.ShouldBeNull();
    }

    [Fact]
    public void reads_batch_rows_reads_optional_review_column()
    {
        var path = WriteTempCsv("source_symbol,target_name,review\nOldName,NewName,check-this");
        var rows = new RenameService().ReadBatchRows(path);
        Assert.Single(rows);
        rows[0].Review.ShouldBe("check-this");
    }

    [Fact]
    public void reads_batch_rows_parses_prefixed_fqn_columns()
    {
        var path = WriteTempCsv("""
                                file,type_fqn,type_name,type_name_new,member_fqn,member_name,member_name_new,skip_reason,sentence_preview
                                tests/FooTests.cs,My.Tests.FooTests,FooTests,Foo,My.Tests.FooTests.Does_work,Does_work,does_work,,Foo does work
                                tests/FooTests.cs,My.Tests.FooTests,FooTests,Foo,My.Tests.FooTests.Reports_failure,Reports_failure,reports_failure,,Foo reports failure
                                tests/BarTests.cs,My.Tests.BarTests,BarTests,Bar,My.Tests.BarTests.With_edge_case,With_edge_case,edge_case,,Bar edge case
                                tests/AlreadyGood.cs,My.Tests.AlreadyGood,AlreadyGood,AlreadyGood,My.Tests.AlreadyGood.already_good,already_good,already_good,,AlreadyGood already good
                                """);

        var rows = new RenameService().ReadBatchRows(path);

        Assert.Collection(
            rows,
            row =>
            {
                row.OldFqn.ShouldBe("My.Tests.FooTests.Does_work");
                row.NewName.ShouldBe("does_work");
                row.AppliedFqn.ShouldBe("My.Tests.Foo.does_work");
            },
            row =>
            {
                row.OldFqn.ShouldBe("My.Tests.FooTests.Reports_failure");
                row.NewName.ShouldBe("reports_failure");
                row.AppliedFqn.ShouldBe("My.Tests.Foo.reports_failure");
            },
            row =>
            {
                row.OldFqn.ShouldBe("My.Tests.BarTests.With_edge_case");
                row.NewName.ShouldBe("edge_case");
                row.AppliedFqn.ShouldBe("My.Tests.Bar.edge_case");
            },
            row =>
            {
                row.OldFqn.ShouldBe("My.Tests.FooTests");
                row.NewName.ShouldBe("Foo");
                row.AppliedFqn.ShouldBe("My.Tests.Foo");
            },
            row =>
            {
                row.OldFqn.ShouldBe("My.Tests.BarTests");
                row.NewName.ShouldBe("Bar");
                row.AppliedFqn.ShouldBe("My.Tests.Bar");
            });
    }

    [Fact]
    public void gets_field_returns_null_for_out_of_bounds_index()
    {
        var fields = new[] { "a", "b" };
        new RenameService().GetField(fields, 5).ShouldBeNull();
        new RenameService().GetField(fields, -1).ShouldBeNull();
    }

    private static string WriteTempCsv(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }
}
