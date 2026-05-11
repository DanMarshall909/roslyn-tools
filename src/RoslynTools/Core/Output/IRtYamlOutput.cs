namespace RoslynTools.Core.Output;

/// <summary>
/// Interface for producing YAML output from Roslyn.
/// </summary>
internal interface IRtYamlOutput
{
    void RenderYaml(TextWriter writer);
}
