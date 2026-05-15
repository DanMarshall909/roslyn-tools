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
}
