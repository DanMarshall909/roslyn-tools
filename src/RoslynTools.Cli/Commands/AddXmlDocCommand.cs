using System.CommandLine;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding XML documentation to the project.
/// </summary>
internal sealed class AddXmlDocCommand : IRtCommand, IRefactorCommand
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly AddXmlDocService _addXmlDoc;

    public AddXmlDocCommand(IWorkspaceSessionFactory sessionFactory, AddXmlDocService addXmlDoc)
    {
        _addXmlDoc = addXmlDoc;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "add-xml-doc",
        true,
        false,
        "refactor",
        Aliases: ["xmldoc"],
        WhenToUse:
        "Bulk-inserting XML <summary> doc comments from an LLM-generated JSON map. " +
        "The JSON file must be a flat object mapping fully-qualified type/member names to summary strings. " +
        "Skips symbols that already have a doc comment. Pair with `rt inspect find-undocumented` to generate the input list.",
        Examples:
        [
            "rt refactor add-xml-doc --from-json docs.json --solution RoslynTools.slnx --dry-run",
            "rt refactor xmldoc --from-json docs.json --solution RoslynTools.slnx"
        ]
    );

    public Command Build()
    {
        var fromJsonOpt = new Option<FileInfo>("--from-json", "JSON file mapping FQN → summary string") { IsRequired = true };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("add-xml-doc", "Insert XML <summary> doc comments from a JSON map");
        cmd.AddAlias("xmldoc");
        cmd.AddAll(fromJsonOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (fromJson, solution, dryRun) =>
        {
            var json = await File.ReadAllTextAsync(fromJson.FullName);
            var docs = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);

            if (docs is null || docs.Count == 0)
            {
                RtLog.Error("No entries found in the JSON file.");
                return;
            }

            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                await _addXmlDoc.RunAsync(sln, docs, dryRun);
            }
        }, fromJsonOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
