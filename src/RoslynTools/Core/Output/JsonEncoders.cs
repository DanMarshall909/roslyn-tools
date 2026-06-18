using System.Text.Encodings.Web;
using System.Text.Json;

namespace RoslynTools.Core.Output;

/// <summary>
///     Single source of truth for JSON encoder selection across rt's three converged
///     output paths:
///     <list type="bullet">
///         <item><see cref="StructuredOutput.Write" /> (CLI anonymous-object writes)</item>
///         <item><see cref="Renderers.JsonRenderer.Write" /> (IRtOutput → JSON)</item>
///         <item>Native MCP handlers (DescribeHandler, OutlineHandler)</item>
///     </list>
///     <para>
///         Default (<paramref name="relaxJson" /> = <c>false</c>) uses
///         <see cref="JavaScriptEncoder.Default" />: non-ASCII and HTML-sensitive
///         characters are escaped as <c>\uXXXX</c>. This is the spec-compliant,
///         maximally portable shape required by strict JSON deserialisers downstream.
///     </para>
///     <para>
///         Opt-in (<paramref name="relaxJson" /> = <c>true</c>, set via the global
///         <c>--relax-json</c> flag) uses
///         <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping" /> for
///         human-readable output: arrows, CJK, and <c>&lt;&gt;'+&amp;</c> render raw.
///     </para>
///     <para>
///         The instances are cached as <c>static readonly</c> because
///         <see cref="JsonSerializerOptions" /> becomes immutable after first use; the
///         helper returns a shared instance per branch rather than constructing a new
///         one per call.
///     </para>
/// </summary>
internal static class JsonEncoders
{
    private static readonly JsonSerializerOptions StrictOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Default
    };

    private static readonly JsonSerializerOptions RelaxedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     Returns the <see cref="JsonSerializerOptions" /> matching the requested
    ///     encoder mode. The result is a shared, immutable instance — do not mutate.
    /// </summary>
    /// <param name="relaxJson">
    ///     <c>true</c> to emit raw non-ASCII / HTML characters
    ///     (<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping" />);
    ///     <c>false</c> for the strict default (<see cref="JavaScriptEncoder.Default" />).
    /// </param>
    internal static JsonSerializerOptions Select(bool relaxJson) =>
        relaxJson ? RelaxedOptions : StrictOptions;
}
