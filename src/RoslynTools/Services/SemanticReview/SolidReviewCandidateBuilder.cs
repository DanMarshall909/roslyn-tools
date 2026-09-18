using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynTools.Services.SemanticReview;

internal sealed record PreparedSolidReviewCandidate(
    SolidReviewPlanItem Plan,
    string SanitizedSource);

internal static class SolidReviewCandidateBuilder
{
    internal const int MaximumSourceBytes = 16_384;

    internal static async Task<IReadOnlyList<PreparedSolidReviewCandidate>> BuildAsync(
        Solution solution,
        SolidResult raw,
        int maxCandidates,
        CancellationToken cancellationToken)
    {
        var rawCandidates = EnumerateRaw(raw)
            .OrderBy(candidate => PrincipleOrder(candidate.Principle))
            .ThenBy(candidate => candidate.Symbol, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Line)
            .ThenBy(candidate => candidate.Kind, StringComparer.Ordinal)
            .ToArray();
        var prepared = new List<PreparedSolidReviewCandidate>(rawCandidates.Length);

        foreach (var candidate in rawCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            prepared.Add(await PrepareAsync(solution, candidate, cancellationToken));
        }

        var admitted = 0;
        for (var index = 0; index < prepared.Count; index++)
        {
            var candidate = prepared[index];
            if (!string.Equals(candidate.Plan.Disposition, "eligible", StringComparison.Ordinal))
                continue;

            if (admitted < maxCandidates)
            {
                admitted++;
                continue;
            }

            prepared[index] = candidate with
            {
                Plan = candidate.Plan with
                {
                    Disposition = "beyond_limit",
                    Reason = $"Candidate exceeds the configured limit of {maxCandidates}."
                }
            };
        }

        return prepared;
    }

    private static IEnumerable<RawSolidCandidate> EnumerateRaw(SolidResult raw)
    {
        foreach (var item in raw.SRP)
        {
            yield return new RawSolidCandidate(
                "SRP",
                item.TypeName,
                item.FilePath,
                item.Line,
                "class",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["publicMethodCount"] = item.PublicMethodCount.ToString(CultureInfo.InvariantCulture),
                    ["dependencyCount"] = item.DependencyCount.ToString(CultureInfo.InvariantCulture),
                    ["detail"] = item.Detail
                });
        }

        foreach (var item in raw.ISP.FatInterfaces)
        {
            yield return new RawSolidCandidate(
                "ISP",
                item.InterfaceName,
                item.FilePath,
                item.Line,
                "fat_interface",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["memberCount"] = item.MemberCount.ToString(CultureInfo.InvariantCulture)
                });
        }

        foreach (var item in raw.ISP.UnimplementedMembers)
        {
            yield return new RawSolidCandidate(
                "ISP",
                $"{item.TypeName}.{item.MemberName}",
                item.FilePath,
                item.Line,
                "unimplemented_member",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["interfaceName"] = item.InterfaceName,
                    ["memberName"] = item.MemberName
                });
        }

        foreach (var item in raw.DIP.ConcreteConstructorParams)
        {
            yield return new RawSolidCandidate(
                "DIP",
                $"{item.TypeName}.ctor({item.ParamName})",
                item.FilePath,
                item.Line,
                "concrete_constructor_parameter",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["parameterName"] = item.ParamName,
                    ["concreteType"] = item.ConcreteType
                });
        }

        foreach (var item in raw.DIP.InlineNewCalls)
        {
            yield return new RawSolidCandidate(
                "DIP",
                $"{item.TypeName}.{item.MemberName}",
                item.FilePath,
                item.Line,
                "inline_construction",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["concreteType"] = item.ConcreteType
                });
        }

        foreach (var item in raw.OCP)
        {
            yield return new RawSolidCandidate(
                "OCP",
                $"{item.TypeName}.{item.MemberName}",
                item.FilePath,
                item.Line,
                item.ViolationType,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["branchCount"] = item.BranchCount.ToString(CultureInfo.InvariantCulture),
                    ["violationType"] = item.ViolationType
                });
        }

        foreach (var item in raw.LSP.ThrowingOverrides)
        {
            yield return new RawSolidCandidate(
                "LSP",
                $"{item.TypeName}.{item.MemberName}",
                item.FilePath,
                item.Line,
                "throwing_override",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["exceptionType"] = item.ExceptionType
                });
        }

        foreach (var item in raw.LSP.ContractNarrowings)
        {
            yield return new RawSolidCandidate(
                "LSP",
                $"{item.TypeName}.{item.MemberName}",
                item.FilePath,
                item.Line,
                "contract_narrowing",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["baseType"] = item.BaseType
                });
        }
    }

    private static async Task<PreparedSolidReviewCandidate> PrepareAsync(
        Solution solution,
        RawSolidCandidate candidate,
        CancellationToken cancellationToken)
    {
        var evidence = new Dictionary<string, string>(candidate.Evidence, StringComparer.Ordinal)
        {
            ["findingKind"] = candidate.Kind
        };
        var document = FindDocument(solution, candidate.FilePath);
        if (document is null)
        {
            return Skipped(
                candidate,
                RelativePath(solution, candidate.FilePath),
                evidence,
                "source_unavailable",
                "The candidate source document was not available in the loaded solution.");
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var text = await document.GetTextAsync(cancellationToken);
        if (root is null || text.Lines.Count == 0)
        {
            return Skipped(
                candidate,
                RelativePath(solution, candidate.FilePath),
                evidence,
                "source_unavailable",
                "The candidate syntax root was unavailable.");
        }

        var lineIndex = Math.Clamp(candidate.Line - 1, 0, text.Lines.Count - 1);
        var position = Math.Min(text.Lines[lineIndex].Start, Math.Max(root.FullSpan.End - 1, 0));
        var token = root.FindToken(position);
        var declaration = SelectDeclaration(token.Parent, candidate.Kind);
        if (declaration is null)
        {
            return Skipped(
                candidate,
                RelativePath(solution, candidate.FilePath),
                evidence,
                "declaration_unavailable",
                "A bounded declaration could not be resolved at the candidate location.");
        }

        var evidenceComplete = true;
        if (candidate.Kind == "fat_interface")
        {
            var usage = await CollectInterfaceUsageAsync(
                solution,
                document,
                declaration,
                cancellationToken);
            evidence["consumerMemberUsage"] = usage.Value;
            evidenceComplete = usage.Complete;
        }

        var sanitizedSource = CandidateSourceSanitizer.Sanitize(declaration.ToFullString());
        var sourceBytes = Encoding.UTF8.GetByteCount(sanitizedSource);
        var hash = ComputeHash(sanitizedSource, evidence);
        var relativePath = RelativePath(solution, candidate.FilePath);
        var candidateId = BuildCandidateId(candidate, relativePath);

        if (sourceBytes > MaximumSourceBytes)
        {
            return new PreparedSolidReviewCandidate(
                new SolidReviewPlanItem(
                    candidateId,
                    candidate.Principle,
                    candidate.Symbol,
                    relativePath,
                    candidate.Line,
                    sourceBytes,
                    hash,
                    evidenceComplete,
                    "skipped",
                    $"Sanitized declaration exceeds the {MaximumSourceBytes} byte limit.",
                    evidence),
                string.Empty);
        }

        if (candidate.Kind == "inline_construction"
            && await IsImmutableValueConstructionAsync(document, declaration, candidate, cancellationToken))
        {
            return new PreparedSolidReviewCandidate(
                new SolidReviewPlanItem(
                    candidateId,
                    candidate.Principle,
                    candidate.Symbol,
                    relativePath,
                    candidate.Line,
                    sourceBytes,
                    hash,
                    evidenceComplete,
                    "prefiltered",
                    "Immutable value construction is low-information dependency evidence.",
                    evidence),
                string.Empty);
        }

        return new PreparedSolidReviewCandidate(
            new SolidReviewPlanItem(
                candidateId,
                candidate.Principle,
                candidate.Symbol,
                relativePath,
                candidate.Line,
                sourceBytes,
                hash,
                evidenceComplete,
                "eligible",
                null,
                evidence),
            sanitizedSource);
    }

    private static SyntaxNode? SelectDeclaration(SyntaxNode? node, string kind)
    {
        if (node is null)
            return null;

        var ancestors = node.AncestorsAndSelf();
        if (kind is "class" or "fat_interface")
            return ancestors.OfType<TypeDeclarationSyntax>().FirstOrDefault();

        return ancestors.OfType<MemberDeclarationSyntax>()
                   .FirstOrDefault(member => member is not TypeDeclarationSyntax)
               ?? ancestors.OfType<TypeDeclarationSyntax>().FirstOrDefault();
    }

    private static async Task<(bool Complete, string Value)> CollectInterfaceUsageAsync(
        Solution solution,
        Document declarationDocument,
        SyntaxNode declaration,
        CancellationToken cancellationToken)
    {
        if (declaration is not InterfaceDeclarationSyntax interfaceDeclaration)
            return (false, "unavailable: declaration is not an interface");

        var declarationModel = await declarationDocument.GetSemanticModelAsync(cancellationToken);
        if (declarationModel?.GetDeclaredSymbol(interfaceDeclaration, cancellationToken)
            is not INamedTypeSymbol interfaceSymbol)
        {
            return (false, "unavailable: interface symbol could not be resolved");
        }

        var consumers = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var member in interfaceSymbol.GetMembers().Where(member => !member.IsImplicitlyDeclared))
        {
            var references = await SymbolFinder.FindReferencesAsync(
                member,
                solution,
                cancellationToken: cancellationToken);
            foreach (var reference in references.SelectMany(found => found.Locations))
            {
                if (!reference.Location.IsInSource)
                    continue;

                var document = solution.GetDocument(reference.Document.Id);
                if (document is null)
                    continue;

                var model = await document.GetSemanticModelAsync(cancellationToken);
                var enclosing = model?.GetEnclosingSymbol(
                    reference.Location.SourceSpan.Start,
                    cancellationToken);
                var consumer = enclosing?.ContainingType;
                if (consumer is null || SymbolEqualityComparer.Default.Equals(consumer, interfaceSymbol))
                    continue;

                var consumerName = consumer.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (!consumers.TryGetValue(consumerName, out var members))
                {
                    members = new SortedSet<string>(StringComparer.Ordinal);
                    consumers[consumerName] = members;
                }

                members.Add(member.Name);
            }
        }

        if (consumers.Count == 0)
            return (false, "unavailable: no exact consumer/member references were found");

        var value = string.Join(
            "; ",
            consumers.Select(pair => $"{pair.Key}: {string.Join(", ", pair.Value)}"));
        return (true, value);
    }

    private static async Task<bool> IsImmutableValueConstructionAsync(
        Document document,
        SyntaxNode declaration,
        RawSolidCandidate candidate,
        CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken);
        if (model is null)
            return false;

        var creation = declaration.DescendantNodesAndSelf()
            .OfType<ObjectCreationExpressionSyntax>()
            .FirstOrDefault(node =>
                node.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == candidate.Line);
        if (creation is null || model.GetTypeInfo(creation, cancellationToken).Type is not INamedTypeSymbol type)
            return false;

        if (type.IsRecord || type.TypeKind == TypeKind.Struct)
            return true;

        var fields = type.GetMembers().OfType<IFieldSymbol>()
            .Where(field => !field.IsStatic && !field.IsImplicitlyDeclared)
            .ToArray();
        var properties = type.GetMembers().OfType<IPropertySymbol>()
            .Where(property => !property.IsStatic && !property.IsImplicitlyDeclared)
            .ToArray();
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => !method.IsStatic
                             && !method.IsImplicitlyDeclared
                             && method.MethodKind == MethodKind.Ordinary)
            .ToArray();

        return fields.Length + properties.Length > 0
               && fields.All(field => field.IsReadOnly)
               && properties.All(property => property.SetMethod is null || property.SetMethod.IsInitOnly)
               && methods.Length == 0;
    }

    private static Document? FindDocument(Solution solution, string filePath)
    {
        var normalized = NormalizePath(filePath);
        return solution.Projects.SelectMany(project => project.Documents)
            .FirstOrDefault(document =>
                document.FilePath is not null
                && string.Equals(
                    NormalizePath(document.FilePath),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string RelativePath(Solution solution, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        var solutionDirectory = Path.GetDirectoryName(solution.FilePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(solutionDirectory))
            return Path.GetFileName(filePath);

        try
        {
            return Path.GetRelativePath(solutionDirectory, filePath).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(filePath);
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return path;
        }
    }

    private static PreparedSolidReviewCandidate Skipped(
        RawSolidCandidate candidate,
        string relativePath,
        IReadOnlyDictionary<string, string> evidence,
        string reasonCode,
        string reason)
    {
        var hash = ComputeHash(string.Empty, evidence);
        return new PreparedSolidReviewCandidate(
            new SolidReviewPlanItem(
                BuildCandidateId(candidate, relativePath),
                candidate.Principle,
                candidate.Symbol,
                relativePath,
                candidate.Line,
                0,
                hash,
                false,
                "skipped",
                $"{reasonCode}: {reason}",
                evidence),
            string.Empty);
    }

    private static string ComputeHash(
        string sanitizedSource,
        IReadOnlyDictionary<string, string> evidence)
    {
        var orderedEvidence = evidence
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var payload = sanitizedSource + "\0" + JsonSerializer.Serialize(orderedEvidence);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static string BuildCandidateId(RawSolidCandidate candidate, string relativePath) =>
        $"{candidate.Principle}:{candidate.Symbol}:{relativePath}:{candidate.Line}:{candidate.Kind}";

    private static int PrincipleOrder(string principle) => principle switch
    {
        "SRP" => 0,
        "ISP" => 1,
        "DIP" => 2,
        "OCP" => 3,
        "LSP" => 4,
        _ => 5
    };

    private sealed record RawSolidCandidate(
        string Principle,
        string Symbol,
        string FilePath,
        int Line,
        string Kind,
        IReadOnlyDictionary<string, string> Evidence);
}
