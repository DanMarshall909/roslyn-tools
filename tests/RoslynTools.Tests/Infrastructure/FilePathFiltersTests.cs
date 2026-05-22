using FPF = RoslynTools.Core.FilePathFilters;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class FilePathFilters_IsGeneratedOrIntermediate
{
    [Fact]
    public void returns_false_for_null() =>
        FPF.IsGeneratedOrIntermediate(null).ShouldBeFalse();

    [Fact]
    public void returns_false_for_empty_string() =>
        FPF.IsGeneratedOrIntermediate(string.Empty).ShouldBeFalse();

    [Fact]
    public void returns_false_for_whitespace_only() =>
        FPF.IsGeneratedOrIntermediate("   ").ShouldBeFalse();

    [Fact]
    public void returns_false_for_plain_cs_file() =>
        FPF.IsGeneratedOrIntermediate("src/RoslynTools/Services/MyService.cs").ShouldBeFalse();

    [Fact]
    public void returns_true_for_path_containing_obj_directory() =>
        FPF.IsGeneratedOrIntermediate("src/RoslynTools/obj/Debug/MyService.cs").ShouldBeTrue();

    [Fact]
    public void returns_true_for_windows_path_with_obj_directory() =>
        FPF.IsGeneratedOrIntermediate(@"src\RoslynTools\obj\Debug\MyService.cs").ShouldBeTrue();

    [Fact]
    public void returns_true_for_g_cs_suffix() =>
        FPF.IsGeneratedOrIntermediate("src/RoslynTools/MyService.g.cs").ShouldBeTrue();

    [Fact]
    public void returns_true_for_generated_cs_suffix() =>
        FPF.IsGeneratedOrIntermediate("src/RoslynTools/MyService.generated.cs").ShouldBeTrue();

    [Fact]
    public void returns_true_for_obj_directory_case_insensitive() =>
        FPF.IsGeneratedOrIntermediate("src/RoslynTools/OBJ/Debug/MyService.cs").ShouldBeTrue();

    [Fact]
    public void returns_true_for_g_cs_suffix_case_insensitive() =>
        FPF.IsGeneratedOrIntermediate("src/RoslynTools/MyService.G.CS").ShouldBeTrue();
}
