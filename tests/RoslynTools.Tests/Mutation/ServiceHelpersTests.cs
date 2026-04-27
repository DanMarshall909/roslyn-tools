using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

/// <summary>
/// Unit tests for pure-logic helper methods extracted into service classes.
/// Integration-level behaviour (workspace write/dry-run) is covered by the
/// existing command-level tests in the same folder.
/// </summary>
public class ServiceHelpersTests
{
    // ── FixNamespaceService ───────────────────────────────────────────────

    [Theory]
    [InlineData("C:/proj/src/Services/Foo.cs", "C:/proj/src", "MyApp", "MyApp.Services")]
    [InlineData("C:/proj/src/Foo.cs", "C:/proj/src", "MyApp", "MyApp")]
    [InlineData("C:/proj/src/A/B/C/Foo.cs", "C:/proj/src", "MyApp", "MyApp.A.B.C")]
    public void ComputeExpectedNamespace_derives_from_folder_path(
        string filePath, string projectDir, string assemblyName, string expected)
    {
        // Normalize to OS path separators
        filePath = filePath.Replace('/', Path.DirectorySeparatorChar);
        projectDir = projectDir.Replace('/', Path.DirectorySeparatorChar);

        var result = FixNamespaceService.ComputeExpectedNamespace(filePath, projectDir, assemblyName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDeclaredNamespace_returns_file_scoped_namespace()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Foo.Bar;");
        var root = tree.GetRoot();
        Assert.Equal("Foo.Bar", FixNamespaceService.GetDeclaredNamespace(root));
    }

    [Fact]
    public void GetDeclaredNamespace_returns_block_scoped_namespace()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Foo.Bar { }");
        var root = tree.GetRoot();
        Assert.Equal("Foo.Bar", FixNamespaceService.GetDeclaredNamespace(root));
    }

    [Fact]
    public void GetDeclaredNamespace_returns_null_when_no_namespace()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Foo { }");
        var root = tree.GetRoot();
        Assert.Null(FixNamespaceService.GetDeclaredNamespace(root));
    }

    // ── AddDependencyService ──────────────────────────────────────────────

    [Theory]
    [InlineData("IEmailService", "_emailService")]
    [InlineData("IFoo", "_foo")]
    [InlineData("EmailService", "_emailService")]
    [InlineData("Abc", "_abc")]
    public void DeriveFieldName_strips_leading_I_for_interfaces(string depType, string expected)
    {
        Assert.Equal(expected, AddDependencyService.DeriveFieldName(depType));
    }

    [Fact]
    public void NormalizeTypeName_strips_global_prefix()
    {
        Assert.Equal("Foo.Bar", AddDependencyService.NormalizeTypeName("global::Foo.Bar"));
        Assert.Equal("Foo", AddDependencyService.NormalizeTypeName("  Foo  "));
    }

    // ── ExtractClassService ───────────────────────────────────────────────

    [Fact]
    public void ExtractClass_DeriveFieldName_returns_camelCase_with_underscore()
    {
        Assert.Equal("_myHelper", ExtractClassService.DeriveFieldName("MyHelper"));
        Assert.Equal("_dependency", ExtractClassService.DeriveFieldName(""));
        Assert.Equal("_a", ExtractClassService.DeriveFieldName("A"));
    }

    [Fact]
    public void ExtractClass_NormalizeTypeName_strips_global_prefix()
    {
        Assert.Equal("My.Type", ExtractClassService.NormalizeTypeName("global::My.Type"));
    }

    [Fact]
    public void BuildNewClassText_generates_static_class_when_flag_set()
    {
        var text = ExtractClassService.BuildNewClassText(
            namespaceName: "MyApp",
            newTypeName: "Helper",
            isStatic: true,
            membersToExtract: []);

        Assert.Contains("namespace MyApp;", text);
        Assert.Contains("public static class Helper", text);
    }

    [Fact]
    public void BuildNewClassText_generates_instance_class_without_namespace()
    {
        var text = ExtractClassService.BuildNewClassText(
            namespaceName: "",
            newTypeName: "Helper",
            isStatic: false,
            membersToExtract: []);

        Assert.DoesNotContain("namespace", text);
        Assert.Contains("public class Helper", text);
        Assert.DoesNotContain("static", text);
    }

    // ── ExtractInterfaceService ───────────────────────────────────────────

    [Fact]
    public void BuildInterfaceText_generates_interface_declaration()
    {
        var text = ExtractInterfaceService.BuildInterfaceText(
            namespaceName: "MyApp",
            interfaceName: "IFoo",
            methods: [],
            properties: []);

        Assert.Contains("namespace MyApp;", text);
        Assert.Contains("public interface IFoo", text);
    }

    [Fact]
    public void BuildInterfaceText_skips_namespace_when_empty()
    {
        var text = ExtractInterfaceService.BuildInterfaceText(
            namespaceName: "",
            interfaceName: "IFoo",
            methods: [],
            properties: []);

        Assert.DoesNotContain("namespace", text);
        Assert.Contains("public interface IFoo", text);
    }

    // ── ReplaceTypeService — rewriter unit tests ───────────────────────────

    [Fact]
    public void DateTimeRewriter_replaces_UtcNow()
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
    public void DateTimeRewriter_replaces_Now()
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
    public void DateTimeRewriter_tracks_class_name()
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
    public void DateTimeRewriter_tracks_struct_class_name()
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
    public void DateTimeRewriter_not_changed_when_no_datetime_usage()
    {
        var tree = CSharpSyntaxTree.ParseText("class Foo { }");
        var root = tree.GetRoot();
        var rewriter = new ReplaceTypeService.DateTimeRewriter();
        rewriter.Visit(root);

        Assert.False(rewriter.Changed);
    }

    // ── RenameService.ReadBatchRows ───────────────────────────────────────

    [Fact]
    public void ReadBatchRows_throws_for_empty_file()
    {
        var path = WriteTempCsv("");
        Assert.Throws<InvalidOperationException>(() => RenameService.ReadBatchRows(path));
    }

    [Fact]
    public void ReadBatchRows_throws_when_source_symbol_column_missing()
    {
        var path = WriteTempCsv("target_name\nFoo");
        Assert.Throws<InvalidOperationException>(() => RenameService.ReadBatchRows(path));
    }

    [Fact]
    public void ReadBatchRows_throws_when_target_name_column_missing()
    {
        var path = WriteTempCsv("source_symbol\nFoo");
        Assert.Throws<InvalidOperationException>(() => RenameService.ReadBatchRows(path));
    }

    [Fact]
    public void ReadBatchRows_parses_valid_csv()
    {
        var path = WriteTempCsv("source_symbol,target_name\nOldName,NewName");
        var rows = RenameService.ReadBatchRows(path);
        Assert.Single(rows);
        Assert.Equal("OldName", rows[0].OldFqn);
        Assert.Equal("NewName", rows[0].NewName);
        Assert.Null(rows[0].Review);
    }

    [Fact]
    public void ReadBatchRows_reads_optional_review_column()
    {
        var path = WriteTempCsv("source_symbol,target_name,review\nOldName,NewName,check-this");
        var rows = RenameService.ReadBatchRows(path);
        Assert.Single(rows);
        Assert.Equal("check-this", rows[0].Review);
    }

    [Fact]
    public void GetField_returns_null_for_out_of_bounds_index()
    {
        var fields = new[] { "a", "b" };
        Assert.Null(RenameService.GetField(fields, 5));
        Assert.Null(RenameService.GetField(fields, -1));
    }

    private static string WriteTempCsv(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }
}
