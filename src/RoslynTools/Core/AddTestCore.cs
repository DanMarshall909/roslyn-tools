using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal static class AddTestCore
{
    internal static async Task RunAsync(
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
            RtLog.Error("Class '{ClassName}' not found in solution.", className);
            Environment.ExitCode = 1;
            return;
        }

        await SolutionWriter.ApplyChangesAsync(originalSolution, updatedSln, dryRun);
    }

    private static async Task<(bool Found, Solution UpdatedSolution)> FindClassAndApplyChanges(
        Solution sln,
        string className,
        string? name,
        string? acs,
        string? nestedClass,
        string? filePath)
    {
        foreach (var project in sln.Projects)
        {
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

                RtLog.Info("Found '{ClassName}' in {FilePath}", className, doc.FilePath);

                var updatedSln = ApplyChangesToDocument(sln, doc, classDecl, className, name, acs, nestedClass);
                return (true, updatedSln);
            }
        }

        return (false, sln);
    }

    private static Solution ApplyChangesToDocument(
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

    private static ClassDeclarationSyntax AppendSingleTest(ClassDeclarationSyntax classDecl, string methodName)
    {
        var method = BuildFactMethod(methodName);
        return classDecl.AddMembers(method);
    }

    private static ClassDeclarationSyntax GenerateBatchTests(
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

    internal static string AcToMethodName(string ac)
    {
        var cleaned = new string(ac.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());

        var words = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());

        return string.Join("_", words);
    }

    private static MemberDeclarationSyntax BuildFactMethod(string methodName)
    {
        var text =
            $"[Fact]\n" +
            $"public void {methodName}()\n" +
            $"{{\n" +
            $"    throw new NotImplementedException();\n" +
            $"}}\n";

        return SyntaxFactory.ParseMemberDeclaration(text)!;
    }

}
