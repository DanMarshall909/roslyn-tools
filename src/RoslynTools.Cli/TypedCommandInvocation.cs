using RoslynTools.Core;

namespace RoslynTools;

/// <summary>
///     Recognizes CLI invocations owned by automatically discovered typed ports so the
///     legacy argv-forwarding path cannot intercept them before their host adapter runs.
/// </summary>
internal static class TypedCommandInvocation
{
    internal static bool IsMatch(
        IReadOnlyList<string> arguments,
        IEnumerable<ApplicationCommandDefinition> definitions)
    {
        if (arguments.Count == 0)
            return false;

        return definitions.Any(definition => IsMatch(arguments, definition.Info));
    }

    private static bool IsMatch(IReadOnlyList<string> arguments, RtCommandInfo info)
    {
        if (MatchesCommand(arguments[0], info))
            return true;

        return arguments.Count > 1
               && string.Equals(arguments[0], info.Category, StringComparison.Ordinal)
               && MatchesCommand(arguments[1], info);
    }

    private static bool MatchesCommand(string argument, RtCommandInfo info) =>
        string.Equals(argument, info.Name, StringComparison.Ordinal)
        || info.Aliases?.Contains(argument, StringComparer.Ordinal) == true;
}
