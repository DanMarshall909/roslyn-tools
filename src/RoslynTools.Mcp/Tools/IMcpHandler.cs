using System.Text.Json;

namespace RoslynTools.Mcp.Tools;

/// <summary>
///     Native MCP handler — bypasses <see cref="CliCommandBridge" />'s text-capture
///     path and returns structured JSON directly. Implement this when a command
///     would benefit from rich, parsed output (rather than CLI-formatted text
///     wrapped in a string).
/// </summary>
/// <remarks>
///     The reference implementation pattern: thin handler that resolves a Core
///     service from DI, parses MCP JSON args into the service's Request DTO, calls
///     the service, serialises the Response. All logic lives in the Core service
///     so the CLI command and this handler share an invocation-agnostic surface.
/// </remarks>
internal interface IMcpHandler
{
    /// <summary>MCP tool name (underscored, e.g. <c>"suggest_command"</c>).</summary>
    string ToolName { get; }

    /// <summary>Schema definition surfaced via <c>tools/list</c>.</summary>
    ToolDefinition Definition { get; }

    /// <summary>Handle the invocation. Return a <see cref="ToolResult" /> whose
    /// <see cref="ToolResult.content" /> is JSON when the handler's contract is
    /// structured.</summary>
    Task<ToolResult> HandleAsync(JsonElement arguments, CancellationToken cancellationToken);
}
