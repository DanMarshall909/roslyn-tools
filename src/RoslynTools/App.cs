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
        root.AddCommand(ExtractInterfaceCommand.Build());
        root.AddCommand(FixNamespaceCommand.Build());
        root.AddCommand(AddDependencyCommand.Build());
        root.AddCommand(FindUnusedCommand.Build());
        root.AddCommand(ExtractClassCommand.Build());
        root.AddCommand(MakeAsyncCommand.Build());
        root.AddCommand(InlineMethodCommand.Build());
        root.AddCommand(SafeDeleteCommand.Build());
        root.AddCommand(ReplaceCallCommand.Build());
        root.AddCommand(AddNullChecksCommand.Build());
        root.AddCommand(MakeSealedCommand.Build());
        root.AddCommand(ConvertToRecordCommand.Build());
        root.AddCommand(ImplementInterfaceCommand.Build());
        root.AddCommand(CircularDepsCommand.Build());
        root.AddCommand(DependencyGraphCommand.Build());
        root.AddCommand(AddTestCommand.Build());

        return await root.InvokeAsync(args);
    }
}
