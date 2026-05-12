using FPF = RoslynTools.Core.FilePathFilters;

namespace RoslynTools.Tests.Infrastructure;

public class FilePathFilters_IsGeneratedOrIntermediate
{
    [Fact]
    public void returns_false_for_null() =>
        Assert.False(FPF.IsGeneratedOrIntermediate(null));

    [Fact]
    public void returns_false_for_empty_string() =>
        Assert.False(FPF.IsGeneratedOrIntermediate(string.Empty));

    [Fact]
    public void returns_false_for_whitespace_only() =>
        Assert.False(FPF.IsGeneratedOrIntermediate("   "));

    [Fact]
    public void returns_false_for_plain_cs_file() =>
        Assert.False(FPF.IsGeneratedOrIntermediate("src/RoslynTools/Services/MyService.cs"));

    [Fact]
    public void returns_true_for_path_containing_obj_directory() =>
        Assert.True(FPF.IsGeneratedOrIntermediate("src/RoslynTools/obj/Debug/MyService.cs"));

    [Fact]
    public void returns_true_for_windows_path_with_obj_directory() =>
        Assert.True(FPF.IsGeneratedOrIntermediate(@"src\RoslynTools\obj\Debug\MyService.cs"));

    [Fact]
    public void returns_true_for_g_cs_suffix() =>
        Assert.True(FPF.IsGeneratedOrIntermediate("src/RoslynTools/MyService.g.cs"));

    [Fact]
    public void returns_true_for_generated_cs_suffix() =>
        Assert.True(FPF.IsGeneratedOrIntermediate("src/RoslynTools/MyService.generated.cs"));

    [Fact]
    public void returns_true_for_obj_directory_case_insensitive() =>
        Assert.True(FPF.IsGeneratedOrIntermediate("src/RoslynTools/OBJ/Debug/MyService.cs"));

    [Fact]
    public void returns_true_for_g_cs_suffix_case_insensitive() =>
        Assert.True(FPF.IsGeneratedOrIntermediate("src/RoslynTools/MyService.G.CS"));
}
