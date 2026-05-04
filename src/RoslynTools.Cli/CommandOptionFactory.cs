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

    /// <summary>Creates a required string option with custom name and help text.</summary>
    public static Option<string> CreateStringOption(string name, string helpText)
        => new(name, helpText) { IsRequired = true };

    /// <summary>Creates an optional nullable string option with custom name and help text.</summary>
    public static Option<string?> CreateNullableStringOption(string name, string helpText)
        => new(name, helpText);

    /// <summary>Creates a required --method option with custom help text.</summary>
    public static Option<string> CreateMethodOption(string helpText)
        => new("--method", helpText) { IsRequired = true };

    /// <summary>Overload supporting refactored code that still passes the option name (for backwards compat during refactoring).</summary>
    public static Option<string> CreateMethodOption(string optionName, string helpText)
    {
        if (optionName != "--method")
            throw new ArgumentException($"Expected '--method' but got '{optionName}'", nameof(optionName));
        return CreateMethodOption(helpText);
    }
}
