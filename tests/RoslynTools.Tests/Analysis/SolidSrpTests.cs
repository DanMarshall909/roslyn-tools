using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core.Output;

namespace RoslynTools.Tests.Analysis;

public class SolidSrpTests
{
    [Fact]
    public void command_info_category_is_analyze()
    {
        Assert.Equal("analyze", SolidCommand.Info.Category);
    }

    [Fact]
    public void command_info_json_output_is_true()
    {
        Assert.True(SolidCommand.Info.JsonOutput);
    }

    [Fact]
    public void command_info_mutates_is_false()
    {
        Assert.False(SolidCommand.Info.Mutates);
    }

    [Fact]
    public void command_info_supported_formats_includes_text_and_json()
    {
        var formats = SolidCommand.Info.SupportedFormats;
        Assert.NotNull(formats);
        Assert.Contains(OutputFormat.Text, formats);
        Assert.Contains(OutputFormat.Json, formats);
    }

    [Fact]
    public void srp_violation_item_has_required_fields()
    {
        var item = new SrpViolationItem
        {
            TypeName = "MyClass",
            FilePath = "src/MyClass.cs",
            Line = 10,
            PublicMethodCount = 7,
            DependencyCount = 4,
            Detail = "Too many responsibilities"
        };

        Assert.Equal("MyClass", item.TypeName);
        Assert.Equal("src/MyClass.cs", item.FilePath);
        Assert.Equal(10, item.Line);
        Assert.Equal(7, item.PublicMethodCount);
        Assert.Equal(4, item.DependencyCount);
        Assert.Equal("Too many responsibilities", item.Detail);
    }

    [Fact]
    public void solid_result_srp_list_is_empty_by_default()
    {
        var result = new SolidResult();
        Assert.Empty(result.SRP);
    }

    [Fact]
    public void solid_result_json_has_top_level_SRP_key()
    {
        var result = new SolidResult();
        using var output = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(result, output);

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.True(doc.RootElement.TryGetProperty("SRP", out _));
    }
}
