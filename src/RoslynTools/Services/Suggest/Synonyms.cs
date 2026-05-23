namespace RoslynTools.Services.Suggest;

/// <summary>
///     Per-command name synonyms used by <see cref="Bm25Scorer" /> to bridge the
///     gap between common LLM phrasings ("remove", "delete") and the canonical
///     command names ("safe-delete"). Hand-maintained; new commands default to
///     just their literal name token if absent here.
/// </summary>
/// <remarks>
///     Keep entries tight — every extra synonym is a chance to mis-rank. Seed
///     only the gaps you've actually observed; do not pre-emptively add synonyms
///     for every English near-equivalent.
/// </remarks>
internal static class Synonyms
{
    public static readonly IReadOnlyDictionary<string, string[]> NameSynonyms =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["safe-delete"] = ["safe-delete", "delete", "remove"],
            ["rename"] = ["rename", "change-name"],
            ["replace-datetime"] = ["replace-datetime", "datetime", "utcnow", "timeprovider", "clock"],
            ["replace-new"] = ["replace-new", "constructor", "new"],
            ["replace-call"] = ["replace-call", "call-site"],
            ["replace-xunit-asserts"] = ["replace-xunit-asserts", "shouldly", "xunit", "assertions"],
            ["replace-type"] = ["replace-type", "swap-type"],
            ["convert-static-class"] = ["convert-static-class", "destatic", "static-to-instance", "static-class"],
            ["convert-to-record"] = ["convert-to-record", "record", "to-record"],
            ["convert-console-logging"] = ["convert-console-logging", "console", "logger", "logging"],
            ["make-async"] = ["make-async", "async", "task"],
            ["make-static"] = ["make-static", "static"],
            ["make-instance"] = ["make-instance", "instance"],
            ["make-sealed"] = ["make-sealed", "sealed"],
            ["extract-method"] = ["extract-method", "extract", "pull-out"],
            ["extract-class"] = ["extract-class", "extract"],
            ["extract-interface"] = ["extract-interface", "interface"],
            ["extract-string-constant"] = ["extract-string-constant", "string-constant", "const-string"],
            ["inline-method"] = ["inline-method", "inline"],
            ["move-method"] = ["move-method", "relocate-method"],
            ["move"] = ["move", "relocate", "namespace"],
            ["fix-namespace"] = ["fix-namespace", "namespace"],
            ["add-dependency"] = ["add-dependency", "inject", "constructor-inject", "dependency-injection", "ctor"],
            ["add-null-checks"] = ["add-null-checks", "null-check", "argumentnullexception", "guard"],
            ["add-xml-doc"] = ["add-xml-doc", "xmldoc", "summary", "documentation"],
            ["add-test"] = ["add-test", "test-stub", "xunit"],
            ["add-global-import"] = ["add-global-import", "global-using"],
            ["add-static-import"] = ["add-static-import", "static-using"],
            ["change-signature"] = ["change-signature", "signature"],
            ["transform-parameters"] = ["transform-parameters", "extract-params", "params-record"],
            ["narrow-dependency"] = ["narrow-dependency", "narrow-interface"],
            ["migrate-to-ioptions"] = ["migrate-to-ioptions", "ioptions", "options-pattern", "configure"],
            ["implement-interface"] = ["implement-interface", "stub", "throw-stub"],
            ["find-refs"] = ["find-refs", "references", "usages", "uses-of"],
            ["find-callers"] = ["find-callers", "callers", "who-calls"],
            ["find-impl"] = ["find-impl", "implementations", "implementors", "subtypes"],
            ["find-pattern"] = ["find-pattern", "structural-match"],
            ["find-unused"] = ["find-unused", "unused", "dead-code"],
            ["find-undocumented"] = ["find-undocumented", "undocumented", "missing-docs"],
            ["find-duplicates"] = ["find-duplicates", "duplicates", "clones", "duplication"],
            ["find-magic-strings"] = ["find-magic-strings", "magic-strings", "string-literals"],
            ["outline"] = ["outline", "members", "type-members", "list-members"],
            ["describe"] = ["describe", "type-shape", "interface-members", "type-info"],
            ["source"] = ["source", "view-source", "show-source"],
            ["circular-deps"] = ["circular-deps", "cycles", "cyclic"],
            ["dependency-graph"] = ["dependency-graph", "deps", "graph"],
            ["analyze-interface"] = ["analyze-interface", "interface-usage", "consumers"],
            ["static-class-inventory"] = ["static-class-inventory", "static-classes", "inventory"],
            ["solid"] = ["solid", "principles", "violations"],
            ["list-smells"] = ["list-smells", "smells", "code-smells"],
            ["complexity"] = ["complexity", "cyclomatic", "cognitive"],
            ["list-imports"] = ["list-imports", "imports", "usings"],
        };
}
