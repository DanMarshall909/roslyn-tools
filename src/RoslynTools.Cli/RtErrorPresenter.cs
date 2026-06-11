using System.CommandLine.Invocation;
using RoslynTools.Core;

namespace RoslynTools.Cli;

/// <summary>
///     The single CLI-side surface for rendering a routine <see cref="RtError" /> returned by a
///     core service: one <c>Error: &lt;message&gt;</c> line to stderr and exit 1. Commands that
///     consume a <see cref="Result{T,TError}" /> call this from the error branch of their
///     <c>Match</c> so there is no per-command if/else error ladder.
/// </summary>
internal static class RtErrorPresenter
{
    internal static void Surface(InvocationContext context, RtError error)
    {
        RtLog.Error("Error: {Message}", error.Message);
        context.ExitCode = 1;
    }
}
