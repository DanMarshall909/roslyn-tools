namespace RoslynTools;

internal static class CliInvocationClassifier
{
    private static readonly HashSet<string> BooleanGlobalOptions = new(StringComparer.Ordinal)
    {
        "--json",
        "--llm",
        "--markdown",
        "--no-suggest",
        "--relax-json",
        "--show-workspace-warnings",
        "--trust-repository",
        "--yaml"
    };

    private static readonly HashSet<string> LocalOnlyRoots = new(StringComparer.Ordinal)
    {
        "config",
        "doctor",
        "dump-help",
        "maintainer",
        "prime",
        "suggest",
        "trust"
    };

    internal static bool RequiresEagerMsBuildRegistration(string[] arguments) =>
        !IsLocalOnly(arguments);

    internal static bool MustRunInProcess(string[] arguments) =>
        IsLocalOnly(arguments) || HasTrustedRoot(arguments);

    internal static bool IsConfigurationInspection(string[] arguments)
    {
        var commandTokens = ExtractCommandTokens(arguments);
        return commandTokens.Count > 0
               && (commandTokens[0] == "config"
                   || commandTokens[0] == "inspect"
                   && commandTokens.ElementAtOrDefault(1) == "config");
    }

    private static bool IsLocalOnly(string[] arguments)
    {
        if (arguments.Length == 0 || arguments.Any(IsHelpOrVersion))
            return true;

        var commandTokens = ExtractCommandTokens(arguments);
        if (commandTokens.Count == 0)
            return false;

        var root = commandTokens[0];
        return LocalOnlyRoots.Contains(root)
               || root == "inspect"
               && commandTokens.ElementAtOrDefault(1) is "config" or "doctor"
               || root == "refactor" && commandTokens.ElementAtOrDefault(1) == "trust";
    }

    private static bool IsHelpOrVersion(string argument) =>
        argument is "-h" or "--help" or "--version";

    private static bool HasTrustedRoot(string[] arguments)
    {
        var commands = ExtractCommandTokens(arguments);
        return commands.Count > 0 && commands[0] == "trusted";
    }

    private static List<string> ExtractCommandTokens(string[] arguments)
    {
        var commands = new List<string>(2);
        using var enumerator = arguments.AsEnumerable().GetEnumerator();
        while (enumerator.MoveNext())
        {
            var argument = enumerator.Current;
            var optionName = argument.Split('=', 2)[0];
            if (BooleanGlobalOptions.Contains(optionName))
                continue;
            if (optionName == "--decisions")
            {
                if (!argument.Contains('=') && !enumerator.MoveNext())
                    return [];

                continue;
            }

            if (argument.StartsWith('-'))
                break;

            commands.Add(argument);
            if (commands.Count == 2)
                return commands;
        }

        return commands;
    }
}
