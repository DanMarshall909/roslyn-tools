using Microsoft.CodeAnalysis;

namespace RoslynTools.Tests;

internal static class TestSolutionDocuments
{
    public static async Task<string> GetTextAsync(Solution solution, string fileName)
    {
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(document => Path.GetFileName(document.FilePath) == fileName);

        return (await document.GetTextAsync()).ToString();
    }
}
