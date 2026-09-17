using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Daemon.Rpc;

/// <summary>Executes one typed daemon request and maps its terminal response to CLI output.</summary>
internal static class DaemonCommandExecutor
{
    internal static async Task<Result<TResponse, RtError>> ExecuteTypedAsync<TRequest, TResponse>(
        DaemonRpcRecoveryCoordinator recovery,
        CommandApplicationPort<TRequest, TResponse> port,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var invocationDirectory = Directory.GetCurrentDirectory();
        if (port.Definition.ExecutionPlacement == CommandExecutionPlacement.InProcess)
            return await port.ExecuteAsync(request, cancellationToken);
        if (IsLocalDiagnosticDoctor(request))
            return await port.ExecuteAsync(request, cancellationToken);

        request = NormalizeInvocationRelativeReadOnlyRequest(request, invocationDirectory);
        if (RepositoryTrustContext.TrustedRepositoryRoot is not null
            || IWorkspaceSessionFactory.HasAmbientOverride
            || DaemonRuntime.IsDisabled)
            return await port.ExecuteAsync(request, cancellationToken);

        var recoveryResult = await recovery.ExecuteAsync(
            port.Definition,
            CreateDaemonArguments(request),
            cancellationToken);
        if (recoveryResult.Response?.Error?.Code == DaemonRpcErrorCodes.CommandNotFound)
            return await port.ExecuteAsync(request, cancellationToken);

        if (recoveryResult.Response is not null)
            return DeserializeResponse<TResponse>(recoveryResult.Response);

        if (!recoveryResult.ShouldFallback)
            return Result<TResponse, RtError>.Err(RtError.ExternalToolFailure(
                "The daemon accepted this request but its result is indeterminate; "
                + "inspect the target before retrying."));

        return await port.ExecuteAsync(request, cancellationToken);
    }

    internal static async Task ExecuteAsync<TRequest>(
        DaemonRpcClient daemon,
        string commandName,
        TRequest request,
        InvocationContext context)
    {
        var safety = DaemonOwnedCommands.All.FirstOrDefault(command =>
            string.Equals(command.Name, commandName, StringComparison.Ordinal))?.Safety
            ?? CommandSafety.ReadOnly;
        var attempt = await daemon.ExecuteAsync(
            commandName,
            JsonSerializer.SerializeToElement(request),
            safety,
            CancellationToken.None);
        if (attempt.Response is null)
        {
            RtLog.Error("The experimental daemon is unavailable. Start it with `rt maintainer daemon start` and retry.");
            context.ExitCode = 1;
            return;
        }

        RenderResponse(attempt.Response, context);
    }

    internal static async Task ExecuteAsync<TRequest>(
        DaemonRpcRecoveryCoordinator recovery,
        ICommandApplicationPort port,
        TRequest request,
        InvocationContext context)
        where TRequest : class
    {
        var cancellationToken = context.GetCancellationToken();
        var invocationDirectory = Directory.GetCurrentDirectory();
        if (port.Definition.ExecutionPlacement == CommandExecutionPlacement.InProcess)
        {
            await ExecuteLocalAsync(port, request, context, cancellationToken);
            return;
        }
        if (IsLocalDiagnosticDoctor(request))
        {
            await ExecuteLocalAsync(port, request, context, cancellationToken);
            return;
        }

        request = NormalizeInvocationRelativeReadOnlyRequest(request, invocationDirectory);
        if (RepositoryTrustContext.TrustedRepositoryRoot is not null
            || IWorkspaceSessionFactory.HasAmbientOverride
            || DaemonRuntime.IsDisabled)
        {
            await ExecuteLocalAsync(port, request, context, cancellationToken);
            return;
        }

        var result = await recovery.ExecuteAsync(
            port.Definition,
            CreateDaemonArguments(request),
            cancellationToken);
        if (result.Response is not null)
        {
            RenderResponse(result.Response, context);
            return;
        }

        if (!result.ShouldFallback)
        {
            RtLog.Error(
                "The daemon accepted this request but its result is indeterminate; inspect the target before retrying.");
            context.ExitCode = 1;
            return;
        }

        await ExecuteLocalAsync(port, request, context, cancellationToken);
    }

    private static TRequest NormalizeInvocationRelativeReadOnlyRequest<TRequest>(
        TRequest request,
        string invocationDirectory)
        where TRequest : class
    {
        if (request is OutlineRequest { Target: { } target } outline
            && !Path.IsPathFullyQualified(target)
            && (!target.EndsWith(".*", StringComparison.Ordinal)
                || target.Contains('/')
                || target.Contains('\\')))
        {
            var candidate = Path.GetFullPath(target, invocationDirectory);
            var isPath = target.Contains('*')
                         || target.Contains('?')
                         || target.Contains(Path.DirectorySeparatorChar)
                         || target.Contains(Path.AltDirectorySeparatorChar)
                         || target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                         || Path.Exists(candidate);
            if (isPath)
                request = (TRequest)(object)(outline with { Target = candidate });
        }

        var node = JsonSerializer.SerializeToNode(request)?.AsObject()
                   ?? throw new JsonException("The command request could not be serialized.");
        if (request is DoctorRequest doctorRequest
            && (doctorRequest.InstallationOnly
                || string.IsNullOrWhiteSpace(doctorRequest.Solution)))
            return request;

        var solutionProperty = node.FirstOrDefault(property => string.Equals(
            property.Key,
            "solution",
            StringComparison.OrdinalIgnoreCase));
        if (solutionProperty.Key is null)
            return request;

        var baseDirectory = request is DoctorRequest doctor
            ? Path.GetFullPath(doctor.Directory, invocationDirectory)
            : invocationDirectory;
        var solution = solutionProperty.Value?.GetValue<string>();
        node[solutionProperty.Key] = SolutionPathResolver.ResolveFrom(solution, baseDirectory);
        return node.Deserialize<TRequest>(DaemonRpcJson.RequestOptions)
               ?? throw new JsonException("The normalized command request could not be deserialized.");
    }

    private static JsonElement CreateDaemonArguments<TRequest>(TRequest request)
        where TRequest : class => JsonSerializer.SerializeToElement(request);

    private static bool IsLocalDiagnosticDoctor<TRequest>(TRequest request) =>
        request is DoctorRequest doctor
        && (doctor.InstallationOnly || string.IsNullOrWhiteSpace(doctor.Solution));

    private static async Task ExecuteLocalAsync<TRequest>(
        ICommandApplicationPort port,
        TRequest request,
        InvocationContext context,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        var local = await port.InvokeAsync(request, cancellationToken);
        if (local.IsError)
        {
            RtLog.Error("Error: {Message}", local.Error?.Message ?? "Command failed.");
            context.ExitCode = 1;
            return;
        }

        var execution = new CommandExecutionResult(local.Value, port.Definition.SuggestedTools);
        RtResultRenderer.Write(RtResult<JsonElement>.Ok(
            DaemonRpcJson.SerializeExecution(execution)));
    }

    private static void RenderResponse(DaemonRpcResponse response, InvocationContext context)
    {
        if (response.Error is not null)
        {
            RtLog.Error("Error: {Message}", response.Error.Message);
            context.ExitCode = 1;
            return;
        }

        RtResultRenderer.Write(RtResult<JsonElement>.Ok(response.Result ?? default));
    }

    private static Result<TResponse, RtError> DeserializeResponse<TResponse>(
        DaemonRpcResponse response)
        where TResponse : class
    {
        if (response.Error is not null)
            return Result<TResponse, RtError>.Err(new RtError(
                response.Error.Kind ?? RtErrorKind.ExternalToolFailure,
                response.Error.Message,
                response.Error.Context));

        if (response.Result is not { } envelope
            || !envelope.TryGetProperty("data", out var data))
            return Result<TResponse, RtError>.Err(RtError.ExternalToolFailure(
                "The daemon returned an invalid command response."));

        try
        {
            var value = data.Deserialize<TResponse>(DaemonRpcJson.Options);
            return value is null
                ? Result<TResponse, RtError>.Err(RtError.ExternalToolFailure(
                    "The daemon returned an empty command result."))
                : Result<TResponse, RtError>.Ok(value);
        }
        catch (JsonException ex)
        {
            return Result<TResponse, RtError>.Err(RtError.ExternalToolFailure(
                $"The daemon returned an invalid command result: {ex.Message}"));
        }
    }
}
