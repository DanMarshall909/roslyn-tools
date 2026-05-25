using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
/// A single proposed mutation that can be independently applied, skipped, or selected
/// via --apply / --skip. Identity is the stable <see cref="Id"/> from <see cref="FixId.Compute"/>.
/// </summary>
internal sealed record Fix(
    string Id,
    string Command,
    string Target,
    string MutationKind,
    IReadOnlyList<FixFile> Files);

/// <summary>
/// One file affected by a <see cref="Fix"/>. Carries the post-mutation source text so the
/// runner can compose a Solution by replacing the document's text, or write the new text directly.
/// </summary>
internal sealed record FixFile(DocumentId DocumentId, string Path, string NewText);
