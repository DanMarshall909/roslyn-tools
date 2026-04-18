using System.CommandLine;

namespace RoslynTools.Core;

internal static class CommandExtensions
{
    internal static void AddAll(this Command cmd, params Option[] options)
    {
        foreach (var opt in options)
            cmd.Add(opt);
    }
}
