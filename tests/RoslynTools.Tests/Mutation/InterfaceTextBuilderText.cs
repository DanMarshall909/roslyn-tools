using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

/// <summary>
///     Direct unit tests for <see cref="InterfaceTextBuilder.BuildInterfaceText" /> that pin the
///     exact textual scaffolding of the generated interface (namespace blank line, opening and
///     closing braces). These kill the Statement/String mutants on the structural AppendLine calls
///     that the higher-level extraction tests cannot observe (they only assert on member names).
/// </summary>
public class InterfaceTextBuilderText
{
    private static string Build(string ns) =>
        InterfaceTextBuilder.BuildInterfaceText(ns, "IFoo", Array.Empty<ISymbol>())
            .Replace("\r\n", "\n");

    [Fact]
    public void emits_blank_line_between_namespace_and_interface()
    {
        var output = Build("MyApp");

        // L20: the blank line (AppendLine()) between the namespace statement and the interface
        // declaration. Removing it collapses the two onto consecutive lines.
        output.ShouldContain("namespace MyApp;\n\npublic interface IFoo");
    }

    [Fact]
    public void emits_opening_brace_on_its_own_line()
    {
        var output = Build("MyApp");

        // L26: sb.AppendLine("{") — Statement-removal drops the brace entirely; String mutation
        // turns "{" into "". Either way the "interface IFoo\n{" shape breaks.
        output.ShouldContain("public interface IFoo\n{");
    }

    [Fact]
    public void emits_closing_brace_as_last_line()
    {
        var output = Build("");

        // L49: sb.AppendLine("}") — Statement-removal drops the closing brace; String mutation
        // empties it. Assert a real "}" line exists AND that it terminates the body.
        var lines = output.TrimEnd('\n').Split('\n');
        lines.ShouldContain("}");
        output.TrimEnd().EndsWith('}').ShouldBeTrue();
    }
}
