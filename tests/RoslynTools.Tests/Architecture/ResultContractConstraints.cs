using System.CommandLine;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architecture gates for the typed result migration. These tests intentionally describe the
///     target shape before the whole codebase is migrated: command handlers should call typed
///     services, services should return results instead of writing to stdout, and renderers should
///     be the only place that turns results into process output.
/// </summary>
public sealed class ResultContractConstraints
{
    [Fact]
    public void core_defines_a_typed_rt_result_contract()
    {
        var coreAssembly = typeof(IRtCommand).Assembly;
        var violations = new List<string>();

        var resultType = coreAssembly.GetType("RoslynTools.Core.RtResult`1");
        if (resultType is null)
        {
            violations.Add("Missing generic result type: RoslynTools.Core.RtResult<T>.");
        }
        else
        {
            RequireProperty(resultType, "Status", violations);
            RequireProperty(resultType, "Value", violations);
            RequireProperty(resultType, "Diagnostics", violations);
            RequireProperty(resultType, "ChangedFiles", violations);
            RequireProperty(resultType, "ExitCode", violations);
        }

        var statusType = coreAssembly.GetType("RoslynTools.Core.RtResultStatus");
        if (statusType is null)
        {
            violations.Add("Missing status enum: RoslynTools.Core.RtResultStatus.");
        }
        else
        {
            foreach (var expected in new[] { "Ok", "NotFound", "Ambiguous", "Unsupported", "Failed" })
                if (!Enum.GetNames(statusType).Contains(expected, StringComparer.Ordinal))
                    violations.Add($"RtResultStatus is missing '{expected}'.");
        }

        Assert.True(
            violations.Count == 0,
            "The migration needs a single typed result contract shared by CLI, MCP, and daemon paths.\n\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void read_only_commands_declare_machine_readable_supported_formats()
    {
        var violations = App.Commands
            .Where(entry => !entry.Info.Mutates)
            .Where(entry => entry.Info.SupportedFormats is null
                            || !entry.Info.SupportedFormats.Contains(OutputFormat.Text)
                            || !entry.Info.SupportedFormats.Contains(OutputFormat.Json))
            .Select(entry =>
            {
                var formats = entry.Info.SupportedFormats is null
                    ? "<null>"
                    : string.Join(", ", entry.Info.SupportedFormats);
                return $"{entry.Info.Name}: SupportedFormats={formats}";
            })
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Every read-only command must declare at least Text and Json in RtCommandInfo.SupportedFormats "
            + "so the result contract is discoverable without parsing help text.\n\n"
            + "Offending commands:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void mutating_commands_expose_dry_run_at_their_invocation_surface()
    {
        var violations = App.Commands
            .Where(entry => entry.Info.Mutates)
            .Where(entry => !App.ControlPlaneCommandNames.Contains(entry.Info.Name))
            .Where(entry => !CommandTreeContainsOption(entry.Build(), "dry-run"))
            .Select(entry => entry.Info.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Every repository-source mutation command must expose --dry-run somewhere on its invocation surface; "
            + "IControlPlaneCommand uses its explicit local verb as the apply decision. "
            + "Mutation planning should be observable before files are written.\n\n"
            + "Offending commands:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void command_handlers_do_not_render_results_directly()
    {
        var commandsDir = Path.Combine(LocateSrcDirectory(), "RoslynTools.Cli", "Commands");
        var violations = FindForbiddenCalls(
            commandsDir,
            file => true,
            IsDirectCommandRenderingCall);

        Assert.True(
            violations.Count == 0,
            "Command handlers should return typed results and let the shared renderer handle process output. "
            + "Direct rendering in commands keeps CLI/MCP/daemon behavior divergent.\n\n"
            + "Offending sites:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void services_do_not_write_primary_results_directly()
    {
        var srcDir = LocateSrcDirectory();
        var serviceDirs = new[]
        {
            Path.Combine(srcDir, "RoslynTools", "Services"),
            Path.Combine(srcDir, "RoslynTools", "Core")
        };

        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(srcDir, "RoslynTools", "Core", "RtConsole.cs"),
            Path.Combine(srcDir, "RoslynTools", "Core", "RtConsoleLogger.cs"),
            Path.Combine(srcDir, "RoslynTools", "Core", "RtLoggerExtensions.cs"),
            Path.Combine(srcDir, "RoslynTools", "Core", "SolutionWriter.cs"),
            // SolutionChangeWriter is the DI-injected wrapper over SolutionWriter; same output-infrastructure role.
            Path.Combine(srcDir, "RoslynTools", "Core", "SolutionChangeWriter.cs")
        };

        var violations = serviceDirs
            .SelectMany(dir => FindForbiddenCalls(
                dir,
                file => !allowedFiles.Contains(file) && !IsOutputInfrastructure(file),
                IsPrimaryResultWrite))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Services should return RtResult<T>/change plans instead of writing primary results directly. "
            + "Only output infrastructure and the final renderer should own stdout formatting.\n\n"
            + "Offending sites:\n  " + string.Join("\n  ", violations));
    }

    private static void RequireProperty(Type type, string propertyName, List<string> violations)
    {
        if (type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance) is null)
            violations.Add($"RtResult<T> is missing public instance property '{propertyName}'.");
    }

    private static bool CommandTreeContainsOption(Command command, string optionName)
    {
        if (command.Options.Any(option => option.Name == optionName))
            return true;

        return command.Children
            .OfType<Command>()
            .Any(child => CommandTreeContainsOption(child, optionName));
    }

    private static List<string> FindForbiddenCalls(
        string rootDirectory,
        Func<string, bool> includeFile,
        Func<InvocationExpressionSyntax, bool> isForbidden)
    {
        var srcDir = LocateSrcDirectory();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(rootDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (!includeFile(file)) continue;

            var rel = Path.GetRelativePath(srcDir, file);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("obj")) continue;
            if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)) continue;

            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!isForbidden(invocation))
                    continue;

                var pos = invocation.GetLocation().GetLineSpan().StartLinePosition;
                violations.Add($"{rel}({pos.Line + 1}): {invocation.Expression}");
            }
        }

        return violations.OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    private static bool IsDirectCommandRenderingCall(InvocationExpressionSyntax invocation)
    {
        return IsStaticCall(invocation, "RtSerializer", "Write")
               || IsStaticCall(invocation, "StructuredOutput", "Write")
               || IsPrimaryResultWrite(invocation)
               || IsStaticCall(invocation, "Environment", "Exit");
    }

    private static bool IsPrimaryResultWrite(InvocationExpressionSyntax invocation)
    {
        if (!IsStaticCall(invocation, "RtConsole", "Write")
            && !IsStaticCall(invocation, "RtConsole", "WriteLine"))
            return false;

        return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString()
            == "RtOutputContext.Result";
    }

    private static bool IsStaticCall(
        InvocationExpressionSyntax invocation,
        string receiver,
        string member)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax id,
            Name.Identifier.Text: var name
        } && id.Identifier.Text == receiver
          && name == member;
    }

    private static bool IsOutputInfrastructure(string file)
    {
        return file.Split(Path.DirectorySeparatorChar)
            .Contains("Output", StringComparer.OrdinalIgnoreCase);
    }

    private static string LocateSrcDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "RoslynTools.slnx")))
                return Path.Combine(dir, "src");
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate RoslynTools.slnx walking up from " + AppContext.BaseDirectory);
    }
}
