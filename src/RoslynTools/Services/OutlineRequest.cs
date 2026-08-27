using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
///     Invocation-agnostic input to the outline operation. CLI's
///     <c>OutlineCommand</c> and MCP's <c>OutlineHandler</c> both build this
///     from their own arg shapes before calling <see cref="OutlineService" />.
/// </summary>
/// <param name="Target">File path, directory, glob, or namespace. Null when <see cref="Regex" /> is set.</param>
/// <param name="Regex">Regex matched against solution file paths. Null when <see cref="Target" /> is set.</param>
/// <param name="Solution">Path to the .sln/.slnx file. Null for auto-detection.</param>
/// <param name="Summary">Compact mode: one entry per top-level type, no member detail.</param>
/// <param name="WithLocations">Legacy verbose mode: grouped members with per-member line ranges.</param>
internal sealed record OutlineRequest(
    [property: CommandPropertyDescription("File path, directory, glob pattern, or namespace to outline")]
    string? Target = null,
    [property: CommandPropertyDescription("Regex pattern matched against solution file paths")]
    string? Regex = null,
    [property: CommandPropertyDescription("Path to the .sln/.slnx file")]
    string? Solution = null,
    [property: CommandPropertyDescription("Compact mode: one entry per top-level type with no member detail")]
    bool Summary = false,
    [property: CommandPropertyDescription("Include grouped members with per-member line ranges")]
    bool WithLocations = false);
