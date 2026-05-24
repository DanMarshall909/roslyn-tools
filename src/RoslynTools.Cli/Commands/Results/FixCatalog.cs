namespace RoslynTools.Commands.Results;

internal static class FixCatalog
{
    private static string Sln(string? solutionPath) =>
        solutionPath is not null ? $"--solution \"{solutionPath}\"" : "--solution <path>";

    internal static IReadOnlyList<SuggestedFix> ForSmell(
        string smellType, string typeName, string? filePath, int line, string? solutionPath)
    {
        var sln = Sln(solutionPath);
        return smellType switch
        {
            "LargeClass" =>
            [
                new SuggestedFix
                {
                    Tool = "rt",
                    Ref = "extract-class",
                    Description = "Move related members into a new dedicated class",
                    CommandLine =
                        $"rt refactor extract-class --type {typeName} --members <members> --new-type <NewTypeName> {sln} --dry-run"
                },
                new SuggestedFix
                {
                    Tool = "resharper",
                    Ref = "Extract Class",
                    Description = "Refactor > Extract Class"
                }
            ],
            "LongMethod" =>
            [
                new SuggestedFix
                {
                    Tool = "rt",
                    Ref = "extract-method",
                    Description = "Pull statements into a new private method",
                    CommandLine =
                        $"rt refactor extract-method \"{filePath}\" <NewMethodName> --start-line {line} --end-line <endLine> {sln} --dry-run"
                },
                new SuggestedFix
                {
                    Tool = "resharper",
                    Ref = "Extract Method",
                    Description = "Refactor > Extract Method (Ctrl+R, M)"
                }
            ],
            "TooManyDependencies" =>
            [
                new SuggestedFix
                {
                    Tool = "rt",
                    Ref = "analyze-interface",
                    Description = "Find narrowing opportunities across consumers",
                    CommandLine = $"rt analyze analyze-interface <ILargeDependency> --suggest {sln}"
                },
                new SuggestedFix
                {
                    Tool = "rt",
                    Ref = "narrow-dependency",
                    Description = "Extract a narrower interface for a member subset",
                    CommandLine =
                        $"rt refactor narrow-dependency <IWideDependency> --members <m1,m2> --new-name <INarrower> {sln} --dry-run"
                }
            ],
            _ => []
        };
    }

    internal static IReadOnlyList<SuggestedFix> ForUnusedSymbol(string kind, string name, string? solutionPath)
    {
        var sln = Sln(solutionPath);
        var kindFlag = kind is "type" or "method" ? $" --kind {kind}" : "";
        return
        [
            new SuggestedFix
            {
                Tool = "rt",
                Ref = "safe-delete",
                Description = "Delete after verifying zero references",
                CommandLine = $"rt refactor safe-delete --symbol {name}{kindFlag} {sln}"
            },
            new SuggestedFix
            {
                Tool = "roslyn-analyzer",
                Ref = kind == "type" ? "CA1812" : "IDE0051",
                Description = kind == "type"
                    ? "Avoid uninstantiated internal classes"
                    : "Remove unused private member"
            }
        ];
    }

    internal static IReadOnlyList<SuggestedFix> ForDuplicateGroup(
        string extractability, string? firstFilePath, int firstLine, int firstEndLine, string? solutionPath)
    {
        if (extractability is "non-extractable" || firstFilePath is null)
            return [];

        var sln = Sln(solutionPath);
        return
        [
            new SuggestedFix
            {
                Tool = "rt",
                Ref = "extract-method",
                Description = "Extract duplicate block into a shared method",
                CommandLine =
                    $"rt refactor extract-method \"{firstFilePath}\" <NewMethodName> --start-line {firstLine} --end-line {firstEndLine} --replace-duplicates {sln} --dry-run"
            }
        ];
    }

    internal static IReadOnlyList<SuggestedFix> ForMagicString(
        string? firstFilePath, int firstLine, string? solutionPath)
    {
        if (firstFilePath is null)
            return [];

        var sln = Sln(solutionPath);
        return
        [
            new SuggestedFix
            {
                Tool = "rt",
                Ref = "extract-string-constant",
                Description = "Replace repeated literal with a named const",
                CommandLine =
                    $"rt refactor extract-string-constant --file \"{firstFilePath}\" --line {firstLine} --name <ConstantName> {sln} --dry-run"
            },
            new SuggestedFix
            {
                Tool = "sonarqube",
                Ref = "S1192",
                Description = "String literals should not be duplicated"
            }
        ];
    }

    internal static IReadOnlyList<SuggestedFix> ForUndocumented(string? solutionPath)
    {
        var sln = Sln(solutionPath);
        return
        [
            new SuggestedFix
            {
                Tool = "rt",
                Ref = "add-xml-doc",
                Description = "Bulk-insert <summary> comments from a JSON map (FQN → summary string)",
                CommandLine = $"rt refactor add-xml-doc --from-json docs.json {sln} --dry-run"
            },
            new SuggestedFix
            {
                Tool = "roslynator",
                Ref = "RCS1232",
                Description = "Add XML documentation comment"
            },
            new SuggestedFix
            {
                Tool = "stylecop",
                Ref = "SA1600",
                Description = "Elements should be documented"
            }
        ];
    }
}
