using System.CommandLine;

namespace RoslynTools.Commands;

internal static class CommandOptionFactory
{
    public static Option<string> CreateFileOption()
        => new("--file", "Path to the existing .cs imports file to update") { IsRequired = true };

    public static Option<string> CreateNamespaceOption()
        => new("--namespace", "Namespace to import globally") { IsRequired = true };

    public static Option<string> CreateTypeOption()
        => new("--type", "Class to convert to a record") { IsRequired = true };

    public static Option<string?> CreateSolutionOption()
        => new("--solution", "Path to .sln/.slnx");

    public static Option<bool> CreateDryRunOption()
        => new("--dry-run", "Preview without writing");

    public static Option<bool> CreateIncludeTestsOption()
        => new("--include-tests", "Include test projects in results");
}
