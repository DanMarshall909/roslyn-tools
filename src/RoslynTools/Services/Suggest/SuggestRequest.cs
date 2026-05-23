namespace RoslynTools.Services.Suggest;

/// <summary>
///     Invocation-agnostic input to <see cref="SuggestService.Suggest" />.
///     CLI and MCP each parse their own surface (positional arg + flag, or JSON
///     property) into this shape before calling the service.
/// </summary>
/// <param name="Query">Natural-language description of the task.</param>
/// <param name="Top">Maximum number of ranked candidates to return.</param>
internal sealed record SuggestRequest(string Query, int Top = 3);
