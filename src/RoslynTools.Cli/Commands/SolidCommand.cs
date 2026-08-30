using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class SolidCommand(
    DaemonRpcRecoveryCoordinator daemon,
    SolidOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly SolidOperation _operation = operation;

    public static RtCommandInfo Info => SolidCommandDefinition.Definition.Info;

    public Command Build()
    {
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx");
        var principleOpt = new Option<string>("--principle", () => "SRP",
            "SOLID principle to check: SRP, ISP, DIP, OCP, LSP, or all");
        var minMethodsOpt = new Option<int>("--min-methods", () => 10,
            "Minimum public method count to flag a class for SRP (default 10)");
        var minDepsOpt = new Option<int>("--min-deps", () => 5,
            "Minimum injected dependency count to flag a class for SRP (default 5)");
        var minMembersOpt = new Option<int>("--min-members", () => 7,
            "Minimum declared interface member count to flag a fat interface for ISP (default 7)");
        var minBranchesOpt = new Option<int>("--min-branches", () => 2,
            "Minimum concrete-type branches to flag a switch or if-chain for OCP (default 2)");

        var cmd = new Command("solid", "Detect SOLID principle violations using heuristic analysis");
        flags.AddTo(cmd);
        cmd.AddAll(principleOpt, minMethodsOpt, minDepsOpt, minMembersOpt, minBranchesOpt);

        cmd.SetHandler(async context =>
        {
            var principle = context.ParseResult.GetValueForOption(principleOpt)!;
            var minMethods = context.ParseResult.GetValueForOption(minMethodsOpt);
            var minDeps = context.ParseResult.GetValueForOption(minDepsOpt);
            var minMembers = context.ParseResult.GetValueForOption(minMembersOpt);
            var minBranches = context.ParseResult.GetValueForOption(minBranchesOpt);
            var request = flags.Bind(
                context.ParseResult,
                f => SolidCliRequest.FromFlags(f, principle, minMethods, minDeps, minMembers, minBranches));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new SolidRequest(
                    request.SolutionPath,
                    request.Principle,
                    request.MinMethods,
                    request.MinDeps,
                    request.MinMembers,
                    request.MinBranches),
                context.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<Commands.Results.SolidResult>.Ok(
                    StaticAnalyzerResultAdapters.Solid(value))),
                error => RtErrorPresenter.Surface(context, error));
        });

        return cmd;
    }

    internal static async Task<int> ExecuteAsync(
        string? solution, string principle, int minMethods, int minDeps, int minMembers, int minBranches,
        IWorkspaceSessionFactory? loader = null)
    {
        if (minMethods < 0)
        {
            RtLog.Error("--min-methods must be >= 0.");
            return 1;
        }

        if (minDeps < 0)
        {
            RtLog.Error("--min-deps must be >= 0.");
            return 1;
        }

        if (minMembers < 0)
        {
            RtLog.Error("--min-members must be >= 0.");
            return 1;
        }

        if (minBranches < 0)
        {
            RtLog.Error("--min-branches must be >= 0.");
            return 1;
        }

        var normalised = principle.ToUpperInvariant();
        if (normalised is not ("SRP" or "ISP" or "DIP" or "OCP" or "LSP" or "ALL"))
        {
            RtLog.Error("--principle must be SRP, ISP, DIP, OCP, LSP, or all.");
            return 1;
        }

        var effectiveLoader = loader ?? IWorkspaceSessionFactory.CreateDefault();
        try
        {
            var operation = new SolidOperation(effectiveLoader);
            var result = await operation.ExecuteAsync(
                new SolidRequest(solution, principle, minMethods, minDeps, minMembers, minBranches));
            return result.Match(
                value =>
                {
                    RtResultRenderer.Write(RtResult<Commands.Results.SolidResult>.Ok(
                        StaticAnalyzerResultAdapters.Solid(value)));
                    return 0;
                },
                error =>
                {
                    RtLog.Error("{Message}", error.Message);
                    return 1;
                });
        }
        finally
        {
            if (loader is null && effectiveLoader is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private sealed record SolidCliRequest(
        string? SolutionPath,
        string Principle,
        int MinMethods,
        int MinDeps,
        int MinMembers,
        int MinBranches) : ISupportsSolutionOption
    {
        public static SolidCliRequest FromFlags(
            RtFlagValues flags,
            string principle,
            int minMethods,
            int minDeps,
            int minMembers,
            int minBranches)
        {
            return new SolidCliRequest(
                flags.SolutionPath,
                principle,
                minMethods,
                minDeps,
                minMembers,
                minBranches);
        }
    }
}
