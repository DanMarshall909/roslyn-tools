using System.Security.Cryptography;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynTools.Core;

/// <summary>
///     Deterministic fingerprint of the loaded source and semantic inputs used by public
///     symbol identities. It is intentionally independent of Roslyn's process-local IDs.
/// </summary>
internal static class SolutionRevision
{
    internal static async Task<string> ComputeAsync(
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "rt.solution.revision.v1");
        Append(hash, solution.FilePath);

        foreach (var project in solution.Projects
                     .OrderBy(project => project.FilePath ?? project.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, project.Name);
            Append(hash, project.AssemblyName);
            Append(hash, project.FilePath);
            Append(hash, project.CompilationOptions?.ToString());
            Append(hash, project.ParseOptions?.ToString());

            foreach (var reference in project.ProjectReferences
                         .Select(reference => solution.GetProject(reference.ProjectId))
                         .OrderBy(reference => reference?.FilePath ?? reference?.Name, StringComparer.Ordinal))
            {
                Append(hash, reference?.FilePath ?? reference?.Name);
            }

            foreach (var reference in project.MetadataReferences
                         .Select(reference => reference.Display)
                         .OrderBy(display => display, StringComparer.Ordinal))
            {
                Append(hash, reference);
                AppendFileIdentity(hash, reference);
            }

            foreach (var reference in project.AnalyzerReferences
                         .Select(reference => reference.FullPath)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                Append(hash, reference);
                AppendFileIdentity(hash, reference);
            }

            await AppendDocumentsAsync(hash, project.Documents, cancellationToken);
            await AppendDocumentsAsync(hash, project.AdditionalDocuments, cancellationToken);
            await AppendDocumentsAsync(hash, project.AnalyzerConfigDocuments, cancellationToken);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task AppendDocumentsAsync(
        IncrementalHash hash,
        IEnumerable<TextDocument> documents,
        CancellationToken cancellationToken)
    {
        foreach (var document in documents
                     .OrderBy(document => document.FilePath ?? document.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, document.Name);
            Append(hash, document.FilePath);
            var text = await document.GetTextAsync(cancellationToken);
            Append(hash, text.ChecksumAlgorithm.ToString());
            Append(hash, text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var checksum = text.GetChecksum();
            if (checksum.Length > 0)
                hash.AppendData(checksum.AsSpan());
            else
                Append(hash, text.ToString());
        }
    }

    private static void AppendFileIdentity(IncrementalHash hash, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        var file = new FileInfo(path);
        Append(hash, file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        hash.AppendData(bytes);
        hash.AppendData([0]);
    }
}
