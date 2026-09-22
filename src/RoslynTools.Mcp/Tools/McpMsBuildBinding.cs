using RoslynTools.Core;
using RoslynTools.Daemon;

namespace RoslynTools.Mcp.Tools;

internal interface IMcpMsBuildBinding
{
    Result<bool, RtError> EnsureRegistered(string directory);
}

internal sealed class McpMsBuildBinding : IMcpMsBuildBinding
{
    private readonly object _gate = new();
    private readonly Func<string, MsBuildInstanceSelection?> _select;
    private MsBuildInstanceIdentity? _registeredIdentity;

    public McpMsBuildBinding()
        : this(
            MsBuildBootstrap.SelectExactInstance,
            MsBuildBootstrap.GetRegisteredSdkIdentity())
    {
    }

    internal McpMsBuildBinding(
        Func<string, MsBuildInstanceSelection?> select,
        MsBuildInstanceIdentity? registeredIdentity = null)
    {
        _select = select;
        _registeredIdentity = registeredIdentity;
    }

    public Result<bool, RtError> EnsureRegistered(string directory)
    {
        MsBuildInstanceSelection? selection;
        try
        {
            selection = _select(directory);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or DllNotFoundException
                                          or InvalidOperationException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return Result<bool, RtError>.Err(MsBuildInstanceCompatibility.SelectionError(
                directory,
                $"Could not select the .NET SDK: {exception.Message}",
                "MCP host",
                "mcpRestartRequired"));
        }

        if (selection is null)
        {
            return Result<bool, RtError>.Err(MsBuildInstanceCompatibility.SelectionError(
                directory,
                "No compatible .NET SDK could be selected.",
                "MCP host",
                "mcpRestartRequired"));
        }

        lock (_gate)
        {
            if (_registeredIdentity is not null)
            {
                return MsBuildInstanceCompatibility.Validate(
                    _registeredIdentity,
                    selection.Identity,
                    directory,
                    "MCP host",
                    "mcpRestartRequired");
            }

            try
            {
                selection.Register();
                _registeredIdentity = selection.Identity;
                return Result<bool, RtError>.Ok(true);
            }
            catch (Exception exception) when (exception is ArgumentException
                                              or DllNotFoundException
                                              or InvalidOperationException
                                              or IOException
                                              or UnauthorizedAccessException)
            {
                return Result<bool, RtError>.Err(MsBuildInstanceCompatibility.SelectionError(
                    directory,
                    $"Could not register the selected .NET SDK: {exception.Message}",
                    "MCP host",
                    "mcpRestartRequired"));
            }
        }
    }

}
