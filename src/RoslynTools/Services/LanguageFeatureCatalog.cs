namespace RoslynTools.Services;

/// <summary>Stable metadata for one detector in the versioned C# feature catalogue.</summary>
internal sealed record LanguageFeatureDefinition(
    string Id,
    string Name,
    string Category,
    string IntroducedIn,
    string DetectorKind,
    string CountingBasis);

/// <summary>
///     The explicit feature inventory understood by <c>language_features</c>. Adding or changing
///     an ID or counting rule requires a catalogue version change.
/// </summary>
internal static class LanguageFeatureCatalog
{
    internal const string Version = "csharp-v1";

    internal static IReadOnlyList<LanguageFeatureDefinition> Definitions { get; } =
    [
        Syntax("file_scoped_namespace", "File-scoped namespace", "declaration", "10.0", "one file-scoped namespace declaration"),
        Syntax("global_using", "Global using directive", "declaration", "10.0", "one global using directive"),
        Syntax("record_declaration", "Record declaration", "declaration", "9.0", "one record class or record struct declaration"),
        Syntax("primary_constructor", "Primary constructor", "declaration", "12.0", "one non-record class or struct primary constructor"),
        Syntax("required_member", "Required member", "declaration", "11.0", "one field or property declaration with the required modifier"),
        Syntax("init_accessor", "Init-only accessor", "declaration", "9.0", "one init accessor declaration"),
        Syntax("file_local_type", "File-local type", "declaration", "11.0", "one type declaration with the file modifier"),
        Syntax("ref_struct", "Ref struct", "declaration", "7.2", "one struct declaration with the ref modifier"),
        Syntax("readonly_struct", "Readonly struct", "declaration", "7.2", "one struct declaration with the readonly modifier"),
        Syntax("function_pointer", "Function pointer", "type", "9.0", "one function pointer type syntax node"),
        Syntax("collection_expression", "Collection expression", "expression", "12.0", "one collection expression syntax node"),
        Syntax("raw_string_literal", "Raw string literal", "expression", "11.0", "one raw string literal expression"),
        Syntax("target_typed_new", "Target-typed new", "expression", "9.0", "one implicit object creation expression"),
        Syntax("with_expression", "With expression", "expression", "9.0", "one with expression syntax node"),
        Syntax("switch_expression", "Switch expression", "expression", "8.0", "one switch expression syntax node"),
        Syntax("range_expression", "Range expression", "expression", "8.0", "one range expression syntax node"),
        Syntax("index_expression", "Index-from-end expression", "expression", "8.0", "one index-from-end expression syntax node"),
        Syntax("null_conditional", "Null-conditional access", "expression", "6.0", "one conditional access expression"),
        Syntax("null_coalescing_assignment", "Null-coalescing assignment", "expression", "8.0", "one null-coalescing assignment expression"),
        Syntax("throw_expression", "Throw expression", "expression", "7.0", "one throw expression syntax node"),
        Syntax("default_literal", "Default literal", "expression", "7.1", "one default literal expression"),
        Syntax("tuple_expression", "Tuple expression", "expression", "7.0", "one tuple expression syntax node"),
        Syntax("anonymous_type", "Anonymous object", "expression", "3.0", "one anonymous object creation expression"),
        Syntax("query_expression", "Query expression", "expression", "3.0", "one query expression syntax node"),
        Syntax("lambda_expression", "Lambda expression", "expression", "3.0", "one lambda expression syntax node"),
        Syntax("object_initializer", "Object initializer", "expression", "3.0", "one object initializer expression"),
        Syntax("collection_initializer", "Collection initializer", "expression", "3.0", "one collection initializer expression"),
        Syntax("interpolated_string", "Interpolated string", "expression", "6.0", "one interpolated string expression"),
        Syntax("list_pattern", "List pattern", "pattern", "11.0", "one list pattern syntax node"),
        Syntax("property_pattern", "Property or positional pattern", "pattern", "8.0", "one recursive pattern syntax node"),
        Syntax("relational_pattern", "Relational pattern", "pattern", "9.0", "one relational pattern syntax node"),
        Syntax("combinator_pattern", "Pattern combinator", "pattern", "9.0", "one and, or, or not pattern syntax node"),
        Syntax("using_declaration", "Using declaration", "statement", "8.0", "one local using declaration"),
        Syntax("await_foreach", "Await foreach", "statement", "8.0", "one await foreach statement"),
        Syntax("await_using", "Await using", "statement", "8.0", "one await using statement or declaration"),
        Syntax("local_function", "Local function", "declaration", "7.0", "one local function declaration"),
        Syntax("expression_bodied_member", "Expression-bodied member", "declaration", "6.0", "one arrow expression clause"),
        Syntax("tuple_type", "Tuple type", "type", "7.0", "one tuple type syntax node"),
        Syntax("async_method", "Async function", "asynchrony", "5.0", "one async method, local function, or anonymous function"),
        Syntax("await_expression", "Await expression", "asynchrony", "5.0", "one await expression syntax node"),
        Semantic("extension_method_invocation", "Extension method invocation", "invocation", "3.0", "one invocation bound as a reduced extension method"),
        ProjectOption("nullable_reference_types", "Nullable reference types", "project-option", "8.0", "one C# project with nullable annotations or warnings enabled"),
        Syntax("params_collection", "Params collection", "declaration", "13.0", "one params parameter whose type is not an array"),
        Syntax("escape_character", "Escape character", "expression", "13.0", "one string or character literal containing the \\e escape"),
        Syntax("partial_property", "Partial property", "declaration", "13.0", "one property declaration with the partial modifier"),
        Syntax("partial_indexer", "Partial indexer", "declaration", "13.0", "one indexer declaration with the partial modifier"),
        Syntax("extension_block", "Extension block", "declaration", "14.0", "one extension block declaration"),
        Syntax("field_backed_property", "Field-backed property", "expression", "14.0", "one field keyword expression"),
        Syntax("null_conditional_assignment", "Null-conditional assignment", "expression", "14.0", "one assignment with a conditional-access target"),
        Syntax("unbound_generic_nameof", "Unbound generic type in nameof", "expression", "14.0", "one nameof expression containing an unbound generic type"),
        Syntax("lambda_parameter_modifier", "Lambda parameter modifier", "declaration", "14.0", "one lambda parameter with a modifier"),
        Syntax("partial_event", "Partial event", "declaration", "14.0", "one event declaration with the partial modifier"),
        Syntax("partial_constructor", "Partial constructor", "declaration", "14.0", "one constructor declaration with the partial modifier"),
        Unsupported("collection_expression_arguments", "Collection expression arguments", "expression", "15.0", "one collection expression with constructor or factory arguments"),
        Unsupported("union_type", "Union type", "type", "15.0", "one union type declaration"),
        Unsupported("closed_hierarchy", "Closed hierarchy", "declaration", "15.0", "one closed type declaration"),
        Unsupported("extension_indexer", "Extension indexer", "declaration", "15.0", "one extension indexer declaration"),
        Unsupported("labeled_break_continue", "Labeled break or continue", "statement", "15.0", "one break or continue statement with a label target"),
        Unsupported("unsafe_expression", "Unsafe expression", "expression", "15.0", "one unsafe expression syntax node"),
        Unsupported("memory_safety_relaxation", "Memory-safety relaxation", "statement", "15.0", "one construct using C# 15 relaxed unsafe-context rules")
    ];

    private static LanguageFeatureDefinition Syntax(
        string id,
        string name,
        string category,
        string introducedIn,
        string countingBasis) => new(id, name, category, introducedIn, "syntax", countingBasis);

    private static LanguageFeatureDefinition Semantic(
        string id,
        string name,
        string category,
        string introducedIn,
        string countingBasis) => new(id, name, category, introducedIn, "semantic", countingBasis);

    private static LanguageFeatureDefinition ProjectOption(
        string id,
        string name,
        string category,
        string introducedIn,
        string countingBasis) => new(id, name, category, introducedIn, "project-option", countingBasis);

    private static LanguageFeatureDefinition Unsupported(
        string id,
        string name,
        string category,
        string introducedIn,
        string countingBasis) => new(id, name, category, introducedIn, "unsupported", countingBasis);
}
