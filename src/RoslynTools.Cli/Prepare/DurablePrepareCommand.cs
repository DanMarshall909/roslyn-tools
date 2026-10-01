using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Cli.Prepare;

/// <summary>
///     Maintainer host adapter for the durable no-effect Prepare boundary. It is not a
///     repository refactor command: its only write is protected local coordination state.
/// </summary>
internal static class DurablePrepareCommand
{
    internal static Command Build(DurablePrepareOperation operation)
    {
        var repositoryIdOpt = RequiredOption<string>(
            "--repository-id",
            "Repository identity carried by the protected stored plan.");
        var planIdOpt = RequiredOption<Guid>(
            "--plan-id",
            "Immutable stored-plan identifier.");
        var planHashOpt = RequiredOption<string>(
            "--plan-hash",
            "SHA-256 hash of the exact canonical stored-plan envelope.");
        var expectedRevisionOpt = RequiredOption<string>(
            "--expected-revision",
            "Repository revision the stored plan was prepared against.");
        var operationEpochOpt = RequiredOption<long>(
            "--operation-epoch",
            "Authenticated operation-epoch number used by the caller-known operation ID.");
        var operationNonceOpt = RequiredOption<Guid>(
            "--operation-nonce",
            "Caller-generated operation nonce.");
        var maximumOperationCountOpt = RequiredOption<int>(
            "--maximum-operation-count",
            "Bound carried by the authenticated operation-epoch claims.");
        var maximumRequestBytesOpt = RequiredOption<long>(
            "--maximum-request-bytes",
            "Canonical request-byte bound carried by the operation-epoch claims.");
        var operationEpochNotAfterOpt = RequiredOption<DateTimeOffset>(
            "--operation-epoch-not-after",
            "UTC expiry carried by the authenticated operation-epoch claims.");
        var dispatchLeaseIdOpt = RequiredOption<string>(
            "--dispatch-lease-id",
            "Stable caller-known dispatch-plan lease ID.");
        var dispatchLeaseExpiresAtOpt = RequiredOption<DateTimeOffset>(
            "--dispatch-lease-expires-at",
            "UTC expiry for the dispatch-plan lease.");
        var intakeAttemptIdOpt = RequiredOption<string>(
            "--intake-attempt-id",
            "Stable transport intake-attempt ID.");
        var intakeAttemptExpiresAtOpt = RequiredOption<DateTimeOffset>(
            "--intake-attempt-expires-at",
            "UTC expiry for the intake-attempt lease.");

        var command = new Command(
            "prepare-operation",
            "Validate an exact stored plan and durably record no-effect Prepared state in protected local storage")
        {
            repositoryIdOpt,
            planIdOpt,
            planHashOpt,
            expectedRevisionOpt,
            operationEpochOpt,
            operationNonceOpt,
            maximumOperationCountOpt,
            maximumRequestBytesOpt,
            operationEpochNotAfterOpt,
            dispatchLeaseIdOpt,
            dispatchLeaseExpiresAtOpt,
            intakeAttemptIdOpt,
            intakeAttemptExpiresAtOpt
        };
        command.SetHandler(async context =>
        {
            var request = new DurablePrepareRequest(
                context.ParseResult.GetValueForOption(repositoryIdOpt)!,
                context.ParseResult.GetValueForOption(planIdOpt),
                context.ParseResult.GetValueForOption(planHashOpt)!,
                context.ParseResult.GetValueForOption(expectedRevisionOpt)!,
                context.ParseResult.GetValueForOption(operationEpochOpt),
                context.ParseResult.GetValueForOption(operationNonceOpt),
                context.ParseResult.GetValueForOption(maximumOperationCountOpt),
                context.ParseResult.GetValueForOption(maximumRequestBytesOpt),
                context.ParseResult.GetValueForOption(operationEpochNotAfterOpt),
                context.ParseResult.GetValueForOption(dispatchLeaseIdOpt)!,
                context.ParseResult.GetValueForOption(dispatchLeaseExpiresAtOpt),
                context.ParseResult.GetValueForOption(intakeAttemptIdOpt)!,
                context.ParseResult.GetValueForOption(intakeAttemptExpiresAtOpt));
            var result = await operation.ExecuteAsync(request, context.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<DurablePrepareResult>.Ok(value)),
                error => RtErrorPresenter.Surface(context, error));
        });
        return command;
    }

    private static Option<T> RequiredOption<T>(string name, string description) => new(name, description)
    {
        IsRequired = true
    };
}
