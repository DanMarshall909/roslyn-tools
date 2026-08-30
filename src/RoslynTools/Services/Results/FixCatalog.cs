using System.Globalization;

namespace RoslynTools.Services.Results;

/// <summary>Creates stable, host-neutral follow-on suggestions for analysis results.</summary>
public static class FixCatalog
{
    private static SuggestedFix Fix(
        string tool,
        string @ref,
        string description,
        string? command = null,
        params SuggestedFixArgument[] arguments) =>
        new()
        {
            Tool = tool,
            Ref = @ref,
            Description = description,
            Command = command,
            Arguments = arguments
        };

    private static SuggestedFixArgument[] WithSolution(
        string? solutionPath,
        SuggestedFixArgument[] arguments,
        params SuggestedFixArgument[] trailingArguments) =>
        [
            .. arguments,
            new("--solution"),
            new(solutionPath ?? "<path>", solutionPath is not null),
            .. trailingArguments
        ];

    public static IReadOnlyList<SuggestedFix> ForSmell(
        string smellType,
        string typeName,
        string? filePath,
        int line,
        string? solutionPath) =>
        smellType switch
        {
            "LargeClass" =>
            [
                Fix(
                    "rt",
                    "extract-class",
                    "Move related members into a new dedicated class",
                    "refactor extract-class",
                    WithSolution(
                        solutionPath,
                        [
                            new("--type"),
                            new(typeName),
                            new("--members"),
                            new("<members>"),
                            new("--new-type"),
                            new("<NewTypeName>")
                        ],
                        new SuggestedFixArgument("--dry-run"))),
                Fix("resharper", "Extract Class", "Refactor > Extract Class")
            ],
            "LongMethod" =>
            [
                Fix(
                    "rt",
                    "extract-method",
                    "Pull statements into a new private method",
                    "refactor extract-method",
                    WithSolution(
                        solutionPath,
                        [
                            new(filePath ?? "<file>", filePath is not null),
                            new("<NewMethodName>"),
                            new("--start-line"),
                            new(line.ToString(CultureInfo.InvariantCulture)),
                            new("--end-line"),
                            new("<endLine>")
                        ],
                        new SuggestedFixArgument("--dry-run"))),
                Fix(
                    "resharper",
                    "Extract Method",
                    "Refactor > Extract Method (Ctrl+R, M)")
            ],
            "TooManyDependencies" =>
            [
                Fix(
                    "rt",
                    "analyze-interface",
                    "Find narrowing opportunities across consumers",
                    "analyze analyze-interface",
                    WithSolution(
                        solutionPath,
                        [new("<ILargeDependency>"), new("--suggest")])),
                Fix(
                    "rt",
                    "narrow-dependency",
                    "Extract a narrower interface for a member subset",
                    "refactor narrow-dependency",
                    WithSolution(
                        solutionPath,
                        [
                            new("<IWideDependency>"),
                            new("--members"),
                            new("<m1,m2>"),
                            new("--new-name"),
                            new("<INarrower>")
                        ],
                        new SuggestedFixArgument("--dry-run")))
            ],
            _ => []
        };

    public static IReadOnlyList<SuggestedFix> ForUnusedSymbol(
        string kind,
        string name,
        string? solutionPath)
    {
        var arguments = new List<SuggestedFixArgument>
        {
            new("--symbol"),
            new(name)
        };
        if (kind is "type" or "method")
        {
            arguments.Add(new("--kind"));
            arguments.Add(new(kind));
        }

        return
        [
            Fix(
                "rt",
                "safe-delete",
                "Delete after verifying zero references",
                "refactor safe-delete",
                WithSolution(solutionPath, [.. arguments])),
            Fix(
                "roslyn-analyzer",
                kind == "type" ? "CA1812" : "IDE0051",
                kind == "type"
                    ? "Avoid uninstantiated internal classes"
                    : "Remove unused private member")
        ];
    }

    public static SuggestedFix ForDynamicDependency(
        string? implementationType,
        string? root,
        string? solutionPath) =>
        Fix(
            "rt",
            "add-dynamic-dependency",
            "Add a durable static reference at the composition root.",
            "refactor add-dynamic-dependency",
            new("--type"),
            new(implementationType ?? "<implementation-type>", true),
            new("--root"),
            new(root ?? "<root>", true),
            new("--solution"),
            new(solutionPath ?? "<path>", true));

    public static IReadOnlyList<SuggestedFix> ForDuplicateGroup(
        string extractability,
        string? firstFilePath,
        int firstLine,
        int firstEndLine,
        string? solutionPath)
    {
        if (extractability is "non-extractable" || firstFilePath is null)
            return [];

        return
        [
            Fix(
                "rt",
                "extract-method",
                "Extract duplicate block into a shared method",
                "refactor extract-method",
                WithSolution(
                    solutionPath,
                    [
                        new(firstFilePath, true),
                        new("<NewMethodName>"),
                        new("--start-line"),
                        new(firstLine.ToString(CultureInfo.InvariantCulture)),
                        new("--end-line"),
                        new(firstEndLine.ToString(CultureInfo.InvariantCulture)),
                        new("--replace-duplicates")
                    ],
                    new SuggestedFixArgument("--dry-run")))
        ];
    }

    public static IReadOnlyList<SuggestedFix> ForMagicString(
        string? firstFilePath,
        int firstLine,
        string? solutionPath)
    {
        if (firstFilePath is null)
            return [];

        return
        [
            Fix(
                "rt",
                "extract-string-constant",
                "Replace repeated literal with a named const",
                "refactor extract-string-constant",
                WithSolution(
                    solutionPath,
                    [
                        new("--file"),
                        new(firstFilePath, true),
                        new("--line"),
                        new(firstLine.ToString(CultureInfo.InvariantCulture)),
                        new("--name"),
                        new("<ConstantName>")
                    ],
                    new SuggestedFixArgument("--dry-run"))),
            Fix(
                "sonarqube",
                "S1192",
                "String literals should not be duplicated")
        ];
    }

    public static IReadOnlyList<SuggestedFix> ForUndocumented(string? solutionPath) =>
        [
            Fix(
                "rt",
                "add-xml-doc",
                "Bulk-insert <summary> comments from a JSON map (FQN → summary string)",
                "refactor add-xml-doc",
                WithSolution(
                    solutionPath,
                    [new("--from-json"), new("docs.json")],
                    new SuggestedFixArgument("--dry-run"))),
            Fix("roslynator", "RCS1232", "Add XML documentation comment"),
            Fix("stylecop", "SA1600", "Elements should be documented")
        ];
}
