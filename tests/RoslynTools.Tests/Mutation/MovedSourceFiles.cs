using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class MovedSourceFiles : IDisposable
{
    private readonly string _projectDir =
        Path.Combine(Path.GetTempPath(), "rt-move-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_projectDir))
                Directory.Delete(_projectDir, true);
        }
        catch
        {
            // Best effort cleanup for files that may still be held by the test host.
        }
    }

    [Theory]
    [InlineData("Sample", "Sample", "")]
    [InlineData("Sample.Features.Orders", "Sample", "Features\\Orders")]
    [InlineData("External.Contracts", "Sample", "External\\Contracts")]
    public void map_namespaces_to_source_folders(string namespaceName, string rootNamespace, string expectedPath)
    {
        var actualPath = new MoveService().NamespaceToRelativeDir(namespaceName, rootNamespace);

        actualPath.ShouldBe(expectedPath.Replace('\\', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void move_files_from_the_old_namespace_folder_to_the_new_folder()
    {
        var oldPath = WriteSource("Features/Orders/Order.cs");

        new MoveService().MoveSourceFiles(
            [oldPath],
            _projectDir,
            "Sample",
            "Sample.Features.Orders",
            "Sample.Domain.Orders",
            false);

        File.Exists(oldPath).ShouldBeFalse();
        File.Exists(Path.Combine(_projectDir, "Domain", "Orders", "Order.cs")).ShouldBeTrue();
    }

    [Fact]
    public void leave_files_in_place_during_a_dry_run()
    {
        var oldPath = WriteSource("Features/Orders/Order.cs");

        new MoveService().MoveSourceFiles(
            [oldPath],
            _projectDir,
            "Sample",
            "Sample.Features.Orders",
            "Sample.Domain.Orders",
            true);

        File.Exists(oldPath).ShouldBeTrue();
        File.Exists(Path.Combine(_projectDir, "Domain", "Orders", "Order.cs")).ShouldBeFalse();
    }

    [Fact]
    public void leave_files_in_place_when_the_target_already_exists()
    {
        var oldPath = WriteSource("Features/Orders/Order.cs");
        var existingTargetPath = WriteSource("Domain/Orders/Order.cs");

        new MoveService().MoveSourceFiles(
            [oldPath],
            _projectDir,
            "Sample",
            "Sample.Features.Orders",
            "Sample.Domain.Orders",
            false);

        File.Exists(oldPath).ShouldBeTrue();
        File.Exists(existingTargetPath).ShouldBeTrue();
    }

    [Fact]
    public void leave_files_in_place_when_their_folder_does_not_match_the_old_namespace()
    {
        var oldPath = WriteSource("Other/Order.cs");

        new MoveService().MoveSourceFiles(
            [oldPath],
            _projectDir,
            "Sample",
            "Sample.Features.Orders",
            "Sample.Domain.Orders",
            false);

        File.Exists(oldPath).ShouldBeTrue();
        File.Exists(Path.Combine(_projectDir, "Domain", "Orders", "Order.cs")).ShouldBeFalse();
    }

    private string WriteSource(string relativePath)
    {
        var fullPath = Path.Combine(_projectDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "namespace Sample;");
        return fullPath;
    }
}
