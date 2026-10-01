using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class MovedSourceFiles
{
    [Theory]
    [InlineData("Sample", "Sample", "")]
    [InlineData("Sample.Features.Orders", "Sample", "Features\\Orders")]
    [InlineData("External.Contracts", "Sample", "External\\Contracts")]
    public void map_namespaces_to_source_folders(string namespaceName, string rootNamespace, string expectedPath)
    {
        var actualPath = new MoveService().NamespaceToRelativeDir(namespaceName, rootNamespace);

        actualPath.ShouldBe(expectedPath.Replace('\\', Path.DirectorySeparatorChar));
    }

}
