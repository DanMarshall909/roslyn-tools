using System.Text.Json;

namespace RoslynTools.Core.Output;

internal static class RtResultRenderer
{
    internal static void Write<T>(RtResult<T> result, JsonSerializerOptions jsonOptions)
    {
        foreach (var diagnostic in result.Diagnostics)
            RtLog.Warning("{Message}", diagnostic);

        Environment.ExitCode = result.ExitCode;

        if (result.Value is not null)
            StructuredOutput.Write(result.Value, jsonOptions);
    }

    internal static void WriteOutput<T>(RtResult<T> result)
        where T : IRtOutput
    {
        foreach (var diagnostic in result.Diagnostics)
            RtLog.Warning("{Message}", diagnostic);

        Environment.ExitCode = result.ExitCode;

        if (result.Value is not null)
            RtSerializer.Write(result.Value);
    }

    internal static void WriteText(RtResult<string> result)
    {
        foreach (var diagnostic in result.Diagnostics)
            RtLog.Warning("{Message}", diagnostic);

        Environment.ExitCode = result.ExitCode;

        if (result.Value is not null)
            RtConsole.WriteLine(RtOutputContext.Result, result.Value);
    }
}
