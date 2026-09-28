namespace RoslynTools.Core.Output;

internal static class RtResultRenderer
{
    internal static void Write<T>(RtResult<T> result)
    {
        foreach (var diagnostic in result.Diagnostics)
            RtLog.Warning("{Message}", diagnostic);

        Environment.ExitCode = result.ExitCode;

        if (result.Value is null)
            return;

        if (result.Value is IRtOutput output)
            RtSerializer.Write(output);
        else
            StructuredOutput.Write(result.Value, SymbolFormatter.DefaultJsonOptions);
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
