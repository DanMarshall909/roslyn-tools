using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;

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
        Assert.Equal(expected, result);
    }

    [Fact]
    public void gets_declared_namespace_returns_file_scoped_namespace()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Foo.Bar;");
        var root = tree.GetRoot();
        Assert.Equal("Foo.Bar", _services.Resolve<FixNamespaceService>().GetDeclaredNamespace(root));
    }

    [Fact]
    public void gets_declared_namespace_returns_block_scoped_namespace()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Foo.Bar { }");
        var root = tree.GetRoot();
        Assert.Equal("Foo.Bar", _services.Resolve<FixNamespaceService>().GetDeclaredNamespace(root));
    }

    [Fact]
    public void gets_declared_namespace_returns_null_when_no_namespace()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Foo { }");
        var root = tree.GetRoot();
        Assert.Null(_services.Resolve<FixNamespaceService>().GetDeclaredNamespace(root));
    }

    // ── AddDependencyService ──────────────────────────────────────────────

    [Theory]
    [InlineData("IEmailService", "_emailService")]
    [InlineData("IFoo", "_foo")]
    [InlineData("EmailService", "_emailService")]
    [InlineData("Abc", "_abc")]
    public void derives_field_name_strips_leading_i_for_interfaces(string depType, string expected)
    {
        Assert.Equal(expected, new AddDependencyService().DeriveFieldName(depType));
    }

    [Fact]
    public void normalize_type_name_strips_global_prefix()
    {
        Assert.Equal("Foo.Bar", new AddDependencyService().NormalizeTypeName("global::Foo.Bar"));
        Assert.Equal("Foo", new AddDependencyService().NormalizeTypeName("  Foo  "));
    }

    // ── ExtractClassService ───────────────────────────────────────────────

    [Fact]
    public void extracts_class_derive_field_name_returns_camel_case_with_underscore()
    {
        Assert.Equal("_myHelper", _services.Resolve<ExtractClassService>().DeriveFieldName("MyHelper"));
        Assert.Equal("_dependency", _services.Resolve<ExtractClassService>().DeriveFieldName(""));
        Assert.Equal("_a", _services.Resolve<ExtractClassService>().DeriveFieldName("A"));
    }

    [Fact]
    public void extracts_class_normalize_type_name_strips_global_prefix()
    {
        Assert.Equal("My.Type", _services.Resolve<ExtractClassService>().NormalizeTypeName("global::My.Type"));
    }

    [Fact]
    public void builds_new_class_text_generates_static_class_when_flag_set()
    {
        var text = _services.Resolve<ExtractClassService>().BuildNewClassText(
            "MyApp",
            "Helper",
            true,
            []);

        Assert.Contains("namespace MyApp;", text);
        Assert.Contains("public static class Helper", text);
    }

    [Fact]
    public void builds_new_class_text_generates_instance_class_without_namespace()
    {
        var text = _services.Resolve<ExtractClassService>().BuildNewClassText(
            "",
            "Helper",
            false,
            []);

        Assert.DoesNotContain("namespace", text);
        Assert.Contains("public class Helper", text);
        Assert.DoesNotContain("static", text);
    }

    // ── ExtractInterfaceService ───────────────────────────────────────────

    [Fact]
    public void builds_interface_text_generates_interface_declaration()
    {
        var text = InterfaceTextBuilder.BuildInterfaceText("MyApp", "IFoo", []);

        Assert.Contains("namespace MyApp;", text);
        Assert.Contains("public interface IFoo", text);
    }

    [Fact]
    public void builds_interface_text_skips_namespace_when_empty()
    {
        var text = InterfaceTextBuilder.BuildInterfaceText("", "IFoo", []);

        Assert.DoesNotContain("namespace", text);
        Assert.Contains("public interface IFoo", text);
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

        Assert.True(rewriter.Changed);
        Assert.Contains("_timeProvider.GetUtcNow().UtcDateTime", newRoot.ToFullString());
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

        Assert.True(rewriter.Changed);
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

        Assert.Contains("OrderService", rewriter.ClassesWithReplacements);
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

        Assert.Contains("TimeStruct", rewriter.ClassesWithReplacements);
    }

    [Fact]
    public void leaves_date_time_rewriter_unchanged_when_no_datetime_usage()
    {
        var tree = CSharpSyntaxTree.ParseText("class Foo { }");
        var root = tree.GetRoot();
        var rewriter = new ReplaceTypeService.DateTimeRewriter();
        rewriter.Visit(root);

        Assert.False(rewriter.Changed);
    }

    // ── RenameService.ReadBatchRows ───────────────────────────────────────

    [Fact]
    public void reads_batch_rows_throws_for_empty_file()
    {
        var path = WriteTempCsv("");
        Assert.Throws<InvalidOperationException>(() => new RenameService().ReadBatchRows(path));
    }

    [Fact]
    public void reads_batch_rows_throws_when_source_symbol_column_missing()
    {
        var path = WriteTempCsv("target_name\nFoo");
        Assert.Throws<InvalidOperationException>(() => new RenameService().ReadBatchRows(path));
    }

    [Fact]
    public void reads_batch_rows_throws_when_target_name_column_missing()
    {
        var path = WriteTempCsv("source_symbol\nFoo");
        Assert.Throws<InvalidOperationException>(() => new RenameService().ReadBatchRows(path));
    }

    [Fact]
    public void reads_batch_rows_parses_valid_csv()
    {
        var path = WriteTempCsv("source_symbol,target_name\nOldName,NewName");
        var rows = new RenameService().ReadBatchRows(path);
        Assert.Single(rows);
        Assert.Equal("OldName", rows[0].OldFqn);
        Assert.Equal("NewName", rows[0].NewName);
        Assert.Null(rows[0].Review);
    }

    [Fact]
    public void reads_batch_rows_reads_optional_review_column()
    {
        var path = WriteTempCsv("source_symbol,target_name,review\nOldName,NewName,check-this");
        var rows = new RenameService().ReadBatchRows(path);
        Assert.Single(rows);
        Assert.Equal("check-this", rows[0].Review);
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
                Assert.Equal("My.Tests.FooTests.Does_work", row.OldFqn);
                Assert.Equal("does_work", row.NewName);
                Assert.Equal("My.Tests.Foo.does_work", row.AppliedFqn);
            },
            row =>
            {
                Assert.Equal("My.Tests.FooTests.Reports_failure", row.OldFqn);
                Assert.Equal("reports_failure", row.NewName);
                Assert.Equal("My.Tests.Foo.reports_failure", row.AppliedFqn);
            },
            row =>
            {
                Assert.Equal("My.Tests.BarTests.With_edge_case", row.OldFqn);
                Assert.Equal("edge_case", row.NewName);
                Assert.Equal("My.Tests.Bar.edge_case", row.AppliedFqn);
            },
            row =>
            {
                Assert.Equal("My.Tests.FooTests", row.OldFqn);
                Assert.Equal("Foo", row.NewName);
                Assert.Equal("My.Tests.Foo", row.AppliedFqn);
            },
            row =>
            {
                Assert.Equal("My.Tests.BarTests", row.OldFqn);
                Assert.Equal("Bar", row.NewName);
                Assert.Equal("My.Tests.Bar", row.AppliedFqn);
            });
    }

    [Fact]
    public void gets_field_returns_null_for_out_of_bounds_index()
    {
        var fields = new[] { "a", "b" };
        Assert.Null(new RenameService().GetField(fields, 5));
        Assert.Null(new RenameService().GetField(fields, -1));
    }

    private static string WriteTempCsv(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }
}
