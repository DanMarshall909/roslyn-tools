using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class AddTestService : IRtService
{
    private readonly ILogger<AddTestService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public AddTestService()
        : this(new RtConsoleLogger<AddTestService>(), new SolutionChangeWriter())
    {
    }

    public AddTestService(ILogger<AddTestService> logger, ISolutionChangeWriter solutionChangeWriter)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    internal async Task RunAsync(
        Solution sln,
        string className,
        string? name,
        string? acs,
        string? nestedClass,
        string? filePath,
        bool dryRun)
    {
        var originalSolution = sln;
        var (found, updatedSln) = await FindClassAndApplyChanges(sln, className, name, acs, nestedClass, filePath);

        if (!found)
        {
            _logger.RtError("Class '{ClassName}' not found in solution.", className);
            Environment.ExitCode = 1;
            return;
        }

        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, updatedSln, dryRun);
    }

    private async Task<(bool Found, Solution UpdatedSolution)> FindClassAndApplyChanges(
        Solution sln,
        string className,
        string? name,
        string? acs,
        string? nestedClass,
        string? filePath)
    {
        foreach (var project in sln.Projects)
        foreach (var doc in project.Documents)
        {
            if (doc.FilePath == null) continue;

            if (filePath != null &&
                !string.Equals(doc.FilePath, Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
                continue;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null) continue;

            var classDecl = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == className);

            if (classDecl == null) continue;

            _logger.RtInfo("Found '{ClassName}' in {FilePath}", className, doc.FilePath);

            var updatedSln = ApplyChangesToDocument(sln, doc, classDecl, className, name, acs, nestedClass);
            return (true, updatedSln);
        }

        return (false, sln);
    }

    private Solution ApplyChangesToDocument(
        Solution sln,
        Document doc,
        ClassDeclarationSyntax classDecl,
        string className,
        string? name,
        string? acs,
        string? nestedClass)
    {
        var cu = (CompilationUnitSyntax)classDecl.SyntaxTree.GetRoot();
        cu = RoslynSyntax.EnsureUsing(cu, "Xunit");
        cu = RoslynSyntax.EnsureUsing(cu, "System");

        // Re-find classDecl after root may have changed
        classDecl = cu.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == className);

        var updatedClass = name != null
            ? AppendSingleTest(classDecl, name)
            : GenerateBatchTests(classDecl, acs!, nestedClass);

        cu = cu.ReplaceNode(classDecl, updatedClass);
        return sln.WithDocumentSyntaxRoot(doc.Id, cu);
    }

    private ClassDeclarationSyntax AppendSingleTest(ClassDeclarationSyntax classDecl, string methodName)
    {
        var method = BuildFactMethod(methodName);
        return classDecl.AddMembers(method);
    }

    private ClassDeclarationSyntax GenerateBatchTests(
        ClassDeclarationSyntax classDecl,
        string acs,
        string? nestedClass)
    {
        var methods = acs
            .Split(';')
            .Select(ac => ac.Trim())
            .Where(ac => ac.Length > 0)
            .Select(AcToMethodName)
            .Select(BuildFactMethod)
            .ToArray();

        if (nestedClass != null)
        {
            var nestedText = $"public class {nestedClass}\n{{\n}}\n";
            var nestedShell = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(nestedText)!;
            var nested = nestedShell.AddMembers(methods);
            return classDecl.AddMembers(nested);
        }

        return classDecl.AddMembers(methods);
    }

    internal string AcToMethodName(string ac)
    {
        var cleaned = new string(ac.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());

        var words = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());

        return string.Join("_", words);
    }

    private MemberDeclarationSyntax BuildFactMethod(string methodName)
    {
        var text =
            $"[Fact]\n" +
            $"public void {methodName}()\n" +
            $"{{\n" +
            $"    throw new NotImplementedException();\n" +
            $"}}\n";

        return SyntaxFactory.ParseMemberDeclaration(text)!;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Command-based test scaffolding
    // ──────────────────────────────────────────────────────────────────────────

    internal async Task RunCommandBasedAsync(
        Solution sln,
        RtCommandInfo commandInfo,
        string? scenario,
        string? outcome,
        string concern,
        string? beforePath,
        string? afterPath,
        bool dryRun)
    {
        var originalSolution = sln;

        // Determine test file path and class name
        var testFilePath = DetermineTestFilePath(commandInfo);
        var className = commandInfo.Name.Split('-')
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
            .Aggregate((a, b) => a + b);

        // Generate method name
        var methodName = scenario != null && outcome != null
            ? ScenarioOutcomeToMethodName(scenario, outcome)
            : "test_case";

        // Generate method with trait
        var method = BuildFactMethodWithTrait(methodName, concern, beforePath, afterPath);

        // Find or create class and apply changes
        var (found, updatedSln) = await FindOrCreateClassAndApplyChanges(
            sln, testFilePath, className, method);

        if (!found)
        {
            _logger.RtError("Failed to add test method to class '{ClassName}'.", className);
            Environment.ExitCode = 1;
            return;
        }

        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, updatedSln, dryRun);
    }

    internal string ScenarioOutcomeToMethodName(string scenario, string outcome)
    {
        // Convert to snake_case with 'with_' prefix
        // "source file as first occurrence" + "updates call site"
        // → "with_source_file_as_first_occurrence_updates_call_site"

        var scenarioSnake = ToSnakeCase(scenario);
        var outcomeSnake = ToSnakeCase(outcome);
        return $"with_{scenarioSnake}_{outcomeSnake}";
    }

    private string ToSnakeCase(string text)
    {
        // Remove non-alphanumeric, split on spaces, join with underscores, lowercase
        var cleaned = new string(text.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("_", words).ToLowerInvariant();
    }

    private MemberDeclarationSyntax BuildFactMethodWithTrait(
        string methodName,
        string concern,
        string? beforePath,
        string? afterPath)
    {
        var beforeComment = beforePath != null ? $"// Transformation test: {beforePath} → {afterPath}\n" : string.Empty;
        var body = beforePath != null
            ? "Assert.Fail(\"TODO\");"
            : "throw new NotImplementedException();";

        var text =
            $"{beforeComment}" +
            $"[Trait(\"Concern\", \"{concern}\")]\n" +
            $"public async Task {methodName}()\n" +
            $"{{\n" +
            $"    {body}\n" +
            $"}}\n";

        return SyntaxFactory.ParseMemberDeclaration(text)!;
    }

    private string DetermineTestFilePath(RtCommandInfo commandInfo)
    {
        // Map category to directory
        var category = commandInfo.Category switch
        {
            "inspect" => "Inspect",
            "analyze" => "Analyze",
            "refactor" => "Refactor",
            _ => "Other"
        };

        // Convert command name from kebab-case to PascalCase for filename
        var fileName = commandInfo.Name.Split('-')
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
            .Aggregate((a, b) => a + b);

        return $"tests/RoslynTools.Tests/{category}/{fileName}.cs";
    }

    private async Task<(bool Found, Solution UpdatedSolution)> FindOrCreateClassAndApplyChanges(
        Solution sln,
        string testFilePath,
        string className,
        MemberDeclarationSyntax method)
    {
        // Try to find existing file and class
        foreach (var project in sln.Projects)
        foreach (var doc in project.Documents)
        {
            if (doc.FilePath == null) continue;

            if (!string.Equals(doc.FilePath, Path.GetFullPath(testFilePath), StringComparison.OrdinalIgnoreCase))
                continue;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null) continue;

            var classDecl = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == className);

            if (classDecl == null) continue;

            _logger.RtInfo("Found '{ClassName}' in {FilePath}", className, doc.FilePath);

            var cu = (CompilationUnitSyntax)root;
            cu = RoslynSyntax.EnsureUsing(cu, "Xunit");
            cu = RoslynSyntax.EnsureUsing(cu, "System");

            classDecl = cu.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .First(c => c.Identifier.Text == className);

            var updatedClass = classDecl.AddMembers(method);
            cu = cu.ReplaceNode(classDecl, updatedClass);

            return (true, sln.WithDocumentSyntaxRoot(doc.Id, cu));
        }

        // File/class not found - log but don't fail (user can manually create if needed)
        _logger.RtInfo("Test class '{ClassName}' not found at {FilePath}. Create the file manually or use legacy mode.",
            className, testFilePath);
        return (false, sln);
    }
}
