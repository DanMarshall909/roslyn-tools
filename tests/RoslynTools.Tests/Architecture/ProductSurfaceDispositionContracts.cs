using Shouldly;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Freezes the unreleased public command inventory against the approved product
///     disposition. A new command must first be added to the product-spine decision table;
///     reflection alone must never silently turn an implementation into a product promise.
/// </summary>
public sealed class ProductSurfaceDispositionContracts
{
    [Fact]
    public void every_public_command_has_exactly_one_approved_disposition()
    {
        var dispositions = ReadCommandDispositions();
        var implementedCommands = App.AllCommands
            .Select(command => command.Info.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        dispositions.Length.ShouldBe(66);
        dispositions.Select(disposition => disposition.Name).ShouldBeUnique();
        dispositions.Select(disposition => disposition.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(implementedCommands);

        var removedByDisposition = dispositions
            .Where(disposition => disposition.Maturity.StartsWith("Removed", StringComparison.Ordinal))
            .Select(disposition => disposition.Name)
            .ToHashSet(StringComparer.Ordinal);
        removedByDisposition.ShouldBe(App.RemovedPublicCommandNames);

        App.Commands.Select(command => command.Info.Name)
            .ShouldNotContain(name => removedByDisposition.Contains(name));
        App.Commands.Select(command => command.Info.Name)
            .ShouldNotContain(name => App.TrustedExperimentalCommandNames.Contains(name));

        foreach (var disposition in dispositions)
        {
            disposition.Destination.ShouldNotBeNullOrWhiteSpace();
            disposition.Maturity.ShouldNotBeNullOrWhiteSpace();
            disposition.Decision.ShouldNotBeNullOrWhiteSpace();
        }
    }

    private static Disposition[] ReadCommandDispositions()
    {
        var path = FindRepositoryFile(
            "openspec", "changes", "shape-pre-release-product-spine", "surface-disposition.md");
        var rows = new List<Disposition>();
        var inCommandInventory = false;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("## Analyze commands", StringComparison.Ordinal))
            {
                inCommandInventory = true;
                continue;
            }

            if (line.StartsWith("## Ancillary commands", StringComparison.Ordinal))
                break;

            if (!inCommandInventory || !line.StartsWith("| `", StringComparison.Ordinal))
                continue;

            var columns = line.Split('|', StringSplitOptions.TrimEntries);
            columns.Length.ShouldBe(6, $"Malformed disposition row: {line}");
            rows.Add(new Disposition(
                columns[1].Trim('`'),
                columns[2],
                columns[3],
                columns[4]));
        }

        return [.. rows];
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(parts)}'.");
    }

    private sealed record Disposition(
        string Name,
        string Destination,
        string Maturity,
        string Decision);
}
