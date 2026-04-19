namespace RoslynTools.Core.Output;

[AttributeUsage(AttributeTargets.Property)]
internal sealed class OutputFieldAttribute : Attribute
{
    internal OutputFieldAttribute(OutputLevel level, string description)
    {
        Level = level;
        Description = description;
    }

    internal OutputLevel Level { get; }
    internal string Description { get; }
}
