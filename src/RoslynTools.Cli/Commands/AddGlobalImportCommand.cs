using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class AddGlobalImportCommand : IRtCommand
{
    private AddGlobalImportCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "add-global-import",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["agi"],
        WhenToUse: "Adding a C# `global using` directive to an existing imports file such as GlobalUsings.cs. Use this when a namespace is intentionally available project-wide.",
        Examples:
        [
            "rt refactor add-global-import --file tests/GlobalUsings.cs --namespace Xunit --solution RoslynTools.slnx",
            "rt refactor agi --file src/App/GlobalUsings.cs --namespace Microsoft.Extensions.Logging --dry-run --solution C:\\code\\app\\App.sln",
            "rt refactor add-global-import --file src/App/GlobalUsings.cs --namespace System.Net.Http.Json --solution C:\\code\\app\\App.sln",
        ]
    );

    public static Command Build()
    {
        var fileOpt = CommandOptionFactory.CreateFileOption();
        var namespaceOpt = CommandOptionFactory.CreateNamespaceOption();
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("add-global-import", "Add a global using directive to an existing file");
        cmd.AddAlias("agi");
        cmd.AddAll(fileOpt, namespaceOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (file, ns, solution, dryRun) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                await RtServiceProvider.Get<ImportDirectiveService>().AddGlobalUsingAsync(sln, file, ns, dryRun);
            }
        }, fileOpt, namespaceOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
