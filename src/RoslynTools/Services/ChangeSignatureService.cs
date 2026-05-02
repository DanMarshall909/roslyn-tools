using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class ChangeSignatureService
{
    internal static async Task<Solution> RenameParamAsync(
        Solution solution,
        string methodSpec,
        string oldName,
        string newName)
    {
        var methodSymbol = await SymbolLocator.FindMethodBySpecAsync(solution, methodSpec);

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

    internal static Task<Solution> ReorderParamsAsync(
        Solution solution,
        string methodSpec,
        string[] newOrder) =>
        ReorderParamsService.ReorderParamsAsync(solution, methodSpec, newOrder);

    internal static Task<Solution> AddParamAsync(
        Solution solution,
        string methodSpec,
        string paramSpec,
        string? afterParam) =>
        AddParamService.AddParamAsync(solution, methodSpec, paramSpec, afterParam);

    internal static Task<Solution> RemoveParamAsync(
        Solution solution,
        string methodSpec,
        string paramName,
        bool force) =>
        RemoveParamService.RemoveParamAsync(solution, methodSpec, paramName, force);
}
