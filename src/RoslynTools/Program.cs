using Microsoft.Build.Locator;
using RoslynTools;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        MsBuildBootstrap.Register();
        return await App.RunAsync(args);
    }
}
