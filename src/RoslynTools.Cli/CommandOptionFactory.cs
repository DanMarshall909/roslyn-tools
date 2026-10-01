using System.CommandLine;

namespace RoslynTools.Commands;

/// <summary>
/// Factory for creating command options.
/// </summary>
internal static class CommandOptionFactory
{
    public static Option<string> CreateFileOption()
    {
        return new Option<string>("--file", "Path to the existing .cs imports file to update") { IsRequired = true };
    }

    public static Option<string> CreateNamespaceOption()
    {
        return new Option<string>("--namespace", "Namespace to import globally") { IsRequired = true };
    }

    public static Option<string> CreateTypeOption()
    {
        return new Option<string>("--type", "Class to convert to a record") { IsRequired = true };
    }

    public static Option<string?> CreateSolutionOption()
    {
        return new Option<string?>("--solution", "Path to .sln/.slnx");
    }

    public static Option<string?> CreateSolutionOption(string helpText)
    {
        return new Option<string?>("--solution", helpText);
    }

    public static Option<bool> CreateDryRunOption()
    {
        return new Option<bool>(
            "--dry-run",
            () => true,
            "Preview without writing (default; pass false to apply explicitly)");
    }

    public static void RejectConflictingApplyAndDryRun(
        Command command,
        Option<bool> dryRunOption,
        Option<bool> applyOption)
    {
        command.AddValidator(result =>
        {
            if (result.FindResultFor(dryRunOption) is { IsImplicit: false }
                && result.GetValueForOption(dryRunOption)
                && result.GetValueForOption(applyOption))
            {
                result.ErrorMessage = "--dry-run and --apply cannot both be enabled.";
            }
        });
    }

    public static Option<bool> CreateIncludeTestsOption()
    {
        return new Option<bool>("--include-tests", "Include test projects in results");
    }

    /// <summary>Creates a required string option with custom name and help text.</summary>
    public static Option<string> CreateStringOption(string name, string helpText)
    {
        return new Option<string>(name, helpText) { IsRequired = true };
    }

    /// <summary>Creates a required --method option with custom help text.</summary>
    public static Option<string> CreateMethodOption(string helpText)
    {
        return new Option<string>("--method", helpText) { IsRequired = true };
    }

    /// <summary>
    ///     Overload supporting refactored code that still passes the option name (for backwards compat during
    ///     refactoring).
    /// </summary>
    public static Option<string> CreateMethodOption(string optionName, string helpText)
    {
        if (optionName != "--method")
            throw new ArgumentException($"Expected '--method' but got '{optionName}'", nameof(optionName));
        return CreateMethodOption(helpText);
    }

    /// <summary>Creates a required --field option with custom help text.</summary>
    public static Option<string?> CreateFieldOption(string helpText)
    {
        return new Option<string?>("--field", helpText);
    }

    /// <summary>Overload supporting refactored code that still passes the option name.</summary>
    public static Option<string?> CreateFieldOption(string optionName, string helpText)
    {
        if (optionName != "--field")
            throw new ArgumentException($"Expected '--field' but got '{optionName}'", nameof(optionName));
        return CreateFieldOption(helpText);
    }

    /// <summary>Creates a required --type option with custom help text.</summary>
    public static Option<string> CreateTypeOption(string helpText)
    {
        return new Option<string>("--type", helpText) { IsRequired = true };
    }

    /// <summary>Overload supporting refactored code that still passes the option name.</summary>
    public static Option<string> CreateTypeOption(string optionName, string helpText)
    {
        if (optionName != "--type")
            throw new ArgumentException($"Expected '--type' but got '{optionName}'", nameof(optionName));
        return CreateTypeOption(helpText);
    }

    /// <summary>Creates an --apply option for batch mutation commands: a CSV of fix IDs to narrow the applied set to.</summary>
    public static Option<string?> CreateApplyOption()
    {
        return new Option<string?>("--apply", "Comma-separated fix IDs to apply (default: all). Mutually exclusive with --skip.");
    }

    /// <summary>Creates a --skip option for batch mutation commands: a CSV of fix IDs to drop from the applied set.</summary>
    public static Option<string?> CreateSkipOption()
    {
        return new Option<string?>("--skip", "Comma-separated fix IDs to skip. Mutually exclusive with --apply.");
    }
}
