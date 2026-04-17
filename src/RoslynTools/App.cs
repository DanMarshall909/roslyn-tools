using System.CommandLine;
using RoslynTools.Commands;

namespace RoslynTools;

internal static class App
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var root = new RootCommand("Roslyn-based C# refactoring tool");

        root.AddCommand(RenameCommand.Build());
        root.AddCommand(MoveCommand.Build());
        root.AddCommand(FindRefsCommand.Build());
        root.AddCommand(InspectCommand.Build());
        root.AddCommand(OutlineCommand.Build());
        root.AddCommand(FindImplementationsCommand.Build());
        root.AddCommand(ReplaceTypeCommand.Build());
        root.AddCommand(ExtractMethodCommand.Build());
        root.AddCommand(FindCallersCommand.Build());
        root.AddCommand(ListSmellsCommand.Build());

        return await root.InvokeAsync(args);
    }
}
