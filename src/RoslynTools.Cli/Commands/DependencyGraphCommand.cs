using System.CommandLine;
using System.Globalization;
using System.Text;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Generates a dependency graph for a solution.
/// </summary>
internal sealed class DependencyGraphCommand(
    DaemonRpcRecoveryCoordinator daemon,
    DependencyGraphOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly DependencyGraphOperation _operation = operation;

    public static RtCommandInfo Info => DependencyGraphCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = new Option<string?>("--type", "Root type to start graph from (optional)");
        var depthOpt = new Option<int?>("--depth", "Maximum traversal depth (optional)");
        var formatOpt = new Option<string?>("--format", "Output format: json (default) or dot");
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");

        var cmd = new Command("dependency-graph", "Build a type-level dependency graph for the solution");
        cmd.AddAlias("depgraph");
        cmd.AddAll(typeOpt, depthOpt, formatOpt, scopeOpt, solutionOpt);

        cmd.SetHandler(async ctx =>
        {
            var typeName = ctx.ParseResult.GetValueForOption(typeOpt);
            var depth = ctx.ParseResult.GetValueForOption(depthOpt);
            var format = ctx.ParseResult.GetValueForOption(formatOpt);
            var scope = ctx.ParseResult.GetValueForOption(scopeOpt);
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);

            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new DependencyGraphRequest(solution, typeName, depth, scope),
                ctx.GetCancellationToken());
            result.Match(
                value => Render(value, format ?? "json"),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private static void Render(DependencyGraphResult result, string format)
    {
        if (!format.Equals("dot", StringComparison.OrdinalIgnoreCase))
        {
            RtResultRenderer.Write(RtResult<DependencyGraphResult>.Ok(result));
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("digraph {");
        foreach (var edge in result.Edges)
            builder.AppendLine(CultureInfo.InvariantCulture, $"  \"{edge.From}\" -> \"{edge.To}\";");

        var nodesInEdges = result.Edges
            .SelectMany(static edge => new[] { edge.From, edge.To })
            .ToHashSet(StringComparer.Ordinal);
        foreach (var node in result.Nodes.Where(node => !nodesInEdges.Contains(node)).OrderBy(static node => node))
            builder.AppendLine(CultureInfo.InvariantCulture, $"  \"{node}\";");

        builder.Append('}');
        RtResultRenderer.WriteText(RtResult<string>.Ok(builder.ToString()));
    }
}
