using System.CommandLine;
using System.CommandLine.Invocation;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class MigrateToIOptionsCommand : IRtCommand
{
    private MigrateToIOptionsCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "migrate-to-ioptions",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["mti"],
        WhenToUse: "Introducing a typed IOptions POCO for an existing configuration section while leaving read-site rewrites as an explicit follow-up worklist.",
        Examples:
        [
            "rt refactor migrate-to-ioptions --section MyFeature --name MyFeatureOptions --solution RoslynTools.slnx",
            "rt refactor migrate-to-ioptions --section MyFeature --name MyFeatureOptions --dry-run",
        ]);

    public static Command Build()
    {
        var sectionOpt = new Option<string?>("--section", "Configuration section name, for example MyFeature or Parent:Child");
        var nameOpt = new Option<string?>("--name", "Options POCO type name in PascalCase");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("migrate-to-ioptions", "Create an options POCO and register it with services.Configure<T>");
        cmd.AddAlias("mti");
        cmd.AddAll(sectionOpt, nameOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (InvocationContext context) =>
        {
            context.ExitCode = await ExecuteAsync(
                context.ParseResult.GetValueForOption(sectionOpt),
                context.ParseResult.GetValueForOption(nameOpt),
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(dryRunOpt));
        });

        return cmd;
    }

    internal static async Task<int> ExecuteAsync(string? section, string? name, string? solutionPath, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            RtLog.Error("--section is required.");
            return 1;
        }

        if (IsMalformedSection(section))
        {
            RtLog.Error("--section is malformed. Use non-empty ':'-separated names without empty segments.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            RtLog.Error("--name is required.");
            return 1;
        }

        if (!IsPascalCase(name))
        {
            RtLog.Error("--name must be PascalCase.");
            return 1;
        }

        try
        {
            var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
            using (workspace)
            {
                return await MigrateToIOptionsService.RunAsync(solution, section, name, dryRun);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            RtLog.Error("Failed to load solution: {Message}", ex.Message);
            return 1;
        }
    }

    private static bool IsMalformedSection(string section) =>
        section.Length == 0
        || section.StartsWith(':')
        || section.EndsWith(':')
        || section.Contains("::", StringComparison.Ordinal);

    private static bool IsPascalCase(string name) =>
        name.Length > 0
        && char.IsUpper(name[0])
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');
}
