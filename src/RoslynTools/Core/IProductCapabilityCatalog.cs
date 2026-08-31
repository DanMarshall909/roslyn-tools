namespace RoslynTools.Core;

/// <summary>Late-bound source of product definitions used by catalogue consumers.</summary>
internal interface IProductCapabilityCatalog
{
    IReadOnlyList<ApplicationCommandDefinition> GetDefinitions();
}
