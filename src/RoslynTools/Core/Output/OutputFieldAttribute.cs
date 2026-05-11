namespace RoslynTools.Core.Output;

/// <summary>
/// Attribute used to mark output fields in C# source files.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class OutputFieldAttribute : Attribute
{
    internal OutputFieldAttribute(OutputLevel level, string description)
    {
        Level = level;
        Description = description;
    }

    /// <summary>
    /// Property representing the output level for the field.
    /// </summary>
    internal OutputLevel Level { get; }
    /// <summary>
    /// Property containing the description for the output field.
    /// </summary>
    internal string Description { get; }
}
