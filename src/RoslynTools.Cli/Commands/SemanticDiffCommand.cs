using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Cli;
using RoslynTools.Services.SemanticDiff;

namespace RoslynTools.Commands;

/// <summary>
///     Thin host adapter for <c>rt analyze semantic-diff</c>: loads two source snapshots
///     from git via <see cref="GitSemanticDiffSourceProvider" />, runs the host-agnostic
///     <see cref="SemanticDiffService" /> engine, and projects the per-declaration deltas as
///     a <see cref="SemanticDiffOutput" />. All three collaborators are constructor-injected
///     from DI; the command owns no comparison logic.
///     <para>
///         Output shape follows the global <c>--json</c> flag via the shared renderer: the
///         default is the git-diff-style text report built by <see cref="SemanticDiffTextRenderer" />,
///         and <c>--json</c> emits a JSON array of deltas (<see cref="RtCommandInfo.JsonOutput" />
///         is <c>true</c> and the command implements <see cref="IStructuredOutputCommand" /> — the
///         two move together to satisfy the <c>App</c> startup biconditional).
///     </para>
/// </summary>
internal sealed class SemanticDiffCommand(SemanticDiffOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly SemanticDiffOperation _operation = operation;

    public static RtCommandInfo Info => HostCommandMetadata.SemanticDiff;

    public Command Build()
    {
        var fromOpt = new Option<string?>(
            "--from",
            "Git ref for the 'before' side. Omit to default to HEAD; with no --to, changed-path discovery remains HEAD-relative.");
        var toOpt = new Option<string?>(
            "--to",
            "Git ref for the 'after' side. Omit to compare against the working tree on Linux x64.");
        var ignoreAccessibilityOpt = new Option<bool>(
            "--ignore-accessibility",
            "Treat declarations that differ only in accessibility modifiers as unchanged.");
        var noDetectRenamesOpt = new Option<bool>(
            "--no-detect-renames",
            "Report a pure rename as a separate Added + Removed pair instead of one Renamed entry.");
        var directoryOpt = new Option<string?>(
            "--directory",
            "Path to the git repository to diff. Omit to use the current directory.");
        var filesArg = new Argument<string[]>(
            "files",
            () => [],
            "Two .cs files to compare directly on disc (works outside git). Omit to diff git versions; "
            + "supply exactly two. Cannot be combined with --from/--to.")
        {
            Arity = ArgumentArity.ZeroOrMore,
        };

        var cmd = new Command(
            "semantic-diff",
            "Semantic C# diff between two git versions or two files on disc; working-tree mode requires Linux x64");
        cmd.AddAlias("sdiff");
        cmd.Add(filesArg);
        cmd.Add(fromOpt);
        cmd.Add(toOpt);
        cmd.Add(ignoreAccessibilityOpt);
        cmd.Add(noDetectRenamesOpt);
        cmd.Add(directoryOpt);

        cmd.SetHandler(async context =>
        {
            var result = await _operation.ExecuteAsync(new SemanticDiffOperationRequest(
                context.ParseResult.GetValueForArgument(filesArg),
                context.ParseResult.GetValueForOption(fromOpt),
                context.ParseResult.GetValueForOption(toOpt),
                context.ParseResult.GetValueForOption(ignoreAccessibilityOpt),
                !context.ParseResult.GetValueForOption(noDetectRenamesOpt),
                context.ParseResult.GetValueForOption(directoryOpt)), context.GetCancellationToken());
            result.Match(
                output => RtResultRenderer.Write(RtResult<SemanticDiffOutput>.Ok(
                    new SemanticDiffOutput(output.Deltas, output.Text))),
                error => RtErrorPresenter.Surface(context, error));
        });

        return cmd;
    }
}
