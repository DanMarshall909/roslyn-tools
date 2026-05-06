using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class ChangeSignatureService : IRtService
{
    private readonly ISymbolResolutionService _symbols;
    private readonly ReorderParamsService _reorderParams;
    private readonly AddParamService _addParam;
    private readonly RemoveParamService _removeParam;

    public ChangeSignatureService()
        : this(new ReorderParamsService(), new AddParamService(), new RemoveParamService())
    {
    }

    public ChangeSignatureService(
        ReorderParamsService reorderParams,
        AddParamService addParam,
        RemoveParamService removeParam,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();

        _reorderParams = reorderParams;
        _addParam = addParam;
        _removeParam = removeParam;
    }

    internal async Task<Solution> RenameParamAsync(
        Solution solution,
        string methodSpec,
        string oldName,
        string newName)
    {
        var methodSymbol = await _symbols.FindMethodBySpecAsync(solution, methodSpec);

        var parameter = methodSymbol.Parameters.FirstOrDefault(param => param.Name == oldName)
            ?? throw new InvalidOperationException(
                $"Parameter '{oldName}' was not found on '{methodSpec}'.");

        if (methodSymbol.Parameters.Any(param => param.Name == newName))
        {
            throw new InvalidOperationException(
                $"Method '{methodSpec}' already has a parameter named '{newName}'.");
        }

        var options = new SymbolRenameOptions(
            RenameOverloads: false,
            RenameInStrings: false,
            RenameInComments: false,
            RenameFile: false);

        return await Renamer.RenameSymbolAsync(solution, parameter, options, newName);
    }

    internal Task<Solution> ReorderParamsAsync(
        Solution solution,
        string methodSpec,
        string[] newOrder) =>
        _reorderParams.ReorderParamsAsync(solution, methodSpec, newOrder);

    internal Task<Solution> AddParamAsync(
        Solution solution,
        string methodSpec,
        string paramSpec,
        string? afterParam) =>
        _addParam.AddParamAsync(solution, methodSpec, paramSpec, afterParam);

    internal Task<Solution> RemoveParamAsync(
        Solution solution,
        string methodSpec,
        string paramName,
        bool force) =>
        _removeParam.RemoveParamAsync(solution, methodSpec, paramName, force);
}
