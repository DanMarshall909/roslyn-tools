using RoslynTools.Core;

namespace RoslynTools;

/// <summary>Projects solution write progress onto the CLI console channels.</summary>
internal sealed class ConsoleSolutionWriteOutput : ISolutionWriteOutput
{
    public void Result(string message) => RtConsole.WriteLine(RtOutputContext.Result, message);

    public void Diff(string message) => RtConsole.WriteLine(RtOutputContext.Diff, message);

    public void DocumentDiff(string oldText, string newText, string filePath) =>
        Adapters.SolutionDiffRenderer.PrintDiff(oldText, newText, filePath, this);

    public void Warning(string message) => RtLog.Warning("{Message}", message);
}
